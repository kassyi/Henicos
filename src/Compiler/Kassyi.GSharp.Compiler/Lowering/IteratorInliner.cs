namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Expands a <c>foreach</c> statement over a <c>[GpuDeviceFunction]</c> iterator into an inline scoped block where each
/// <c>yield return</c> is replaced by the consumer body and <c>yield break</c> / consumer <c>break</c> / <c>continue</c>
/// become structured forward <c>goto</c> statements.
/// </summary>
internal static class IteratorInliner
{
    /// <summary>Maximum number of consumer <c>foreach</c> body copies before reporting GSH0073.</summary>
    public const int MaxBodyCopies = 32;

    /// <summary>Unsupported syntax reason reported when a <c>foreach</c> targets an iterator whose body is not in the compilation.</summary>
    public const string IteratorWithoutBody = "foreach of an iterator that has no body in this compilation";

    /// <summary>
    /// Resolves the source <see cref="MethodDeclarationSyntax"/> of <paramref name="iteratorMethod"/> when it belongs
    /// to <paramref name="compilation"/> and has a block body.
    /// </summary>
    public static bool TryGetInlinableSyntax(
        IMethodSymbol iteratorMethod,
        Compilation compilation,
        [NotNullWhen(true)] out MethodDeclarationSyntax? iteratorDecl)
    {
        if (iteratorMethod.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var decl) &&
            decl?.Body != null &&
            compilation.ContainsSyntaxTree(decl.SyntaxTree))
        {
            iteratorDecl = decl;
            return true;
        }

        iteratorDecl = null;
        return false;
    }

    /// <summary>
    /// Expands <paramref name="iteratorDecl"/> with a synthetic <c>foreach</c> over its own parameters and an empty
    /// consumer body so that <see cref="Transpiler.MethodTranspiler"/> and
    /// <see cref="Diagnostics.Kernel.TranspilerFailureCheck"/> can validate the iterator body once at its declaration site.
    /// </summary>
    public static BlockSyntax ExpandStandalone(
        MethodDeclarationSyntax iteratorDecl,
        IMethodSymbol iteratorMethod,
        in IteratorExpansionContext context,
        ref int regionCounter,
        ref int argumentTempCounter)
    {
        var arguments = ImmutableArray.CreateBuilder<ArgumentSyntax>(iteratorDecl.ParameterList.Parameters.Count);
        foreach (var parameter in iteratorDecl.ParameterList.Parameters)
        {
            arguments.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(parameter.Identifier)));
        }

        var syntheticInvocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(iteratorDecl.Identifier),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments))
        );
        var syntheticForEach = SyntaxFactory.ForEachStatement(
            SyntaxFactory.IdentifierName("var"),
            SyntaxFactory.Identifier("__gsharp_item"),
            syntheticInvocation,
            SyntaxFactory.Block()
        );

        return Expand(
            syntheticForEach,
            iteratorMethod,
            syntheticInvocation,
            context,
            ref regionCounter,
            ref argumentTempCounter);
    }

    /// <summary>
    /// Expands <paramref name="forEachNode"/> over <paramref name="iteratorMethod"/> into a two-statement block consisting
    /// of the scoped expansion block and the trailing <c>__gsharp_iter_end_&lt;r&gt;:;</c> label.
    /// </summary>
    public static BlockSyntax Expand(
        ForEachStatementSyntax forEachNode,
        IMethodSymbol iteratorMethod,
        InvocationExpressionSyntax sourceInvocation,
        in IteratorExpansionContext context,
        ref int regionCounter,
        ref int argumentTempCounter)
    {
        var semanticModel = context.SemanticModel;
        var compilation = semanticModel.Compilation;
        if (!TryGetInlinableSyntax(iteratorMethod, compilation, out var iteratorDecl) ||
            iteratorDecl.Body is not { } iteratorBody)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, forEachNode, IteratorWithoutBody);
            return SyntaxFactory.Block();
        }

        var annotatedForEach = compilation.ContainsSyntaxTree(forEachNode.SyntaxTree)
            ? LoweringAnnotations.AnnotateOriginalSpans(forEachNode, compilation)
            : forEachNode;
        var iteratorRegion = ++regionCounter;
        var (stampedParamList, stampedBody) = CopyIteratorBody(iteratorDecl, iteratorBody, iteratorRegion, semanticModel);

        var loopVariableType = ResolveLoopVariableType(annotatedForEach, iteratorDecl, compilation);

        var expansionStatements = ImmutableArray.CreateBuilder<StatementSyntax>();
        var site = new IteratorCallSite(annotatedForEach, iteratorMethod, sourceInvocation);
        AddParameterLocals(expansionStatements, site, stampedParamList, context, ref argumentTempCounter);

        var endLabel = $"{GSharpInternalNames.Iterator.EndLabelPrefix}{iteratorRegion}";
        var yieldRewriter = new IteratorYieldRewriter(
            annotatedForEach,
            loopVariableType,
            NeedsElementCast(annotatedForEach, iteratorMethod, semanticModel),
            iteratorRegion,
            endLabel,
            semanticModel);
        var rewrittenBody = yieldRewriter.Rewrite(stampedBody, ref regionCounter);
        expansionStatements.AddRange(rewrittenBody.Statements);

        // { <paramLocals>; <rewrittenBody> } __gsharp_iter_end_<r>: ;
        var expansionBlock = SyntaxFactory.Block(expansionStatements)
            .WithAdditionalAnnotations(new SyntaxAnnotation(LoweringAnnotations.ScopedBlockAnnotationKind));
        var endLabeledStmt = SyntaxFactory.LabeledStatement(
            SyntaxFactory.Identifier(endLabel),
            SyntaxFactory.EmptyStatement()
        );

        return SyntaxFactory.Block(expansionBlock, endLabeledStmt);
    }

    /// <summary>
    /// Copies the parameter list and body of the iterator out of the compilation: annotated with their original spans,
    /// without Stryker's mutant switches, and stamped with <paramref name="iteratorRegion"/>.
    /// </summary>
    private static (ParameterListSyntax ParameterList, BlockSyntax Body) CopyIteratorBody(
        MethodDeclarationSyntax iteratorDecl,
        BlockSyntax iteratorBody,
        int iteratorRegion,
        SemanticModel semanticModel)
    {
        var compilation = semanticModel.Compilation;
        var annotatedParamList = LoweringAnnotations.AnnotateOriginalSpans(iteratorDecl.ParameterList, compilation);
        // The iterator body is copied into the consumer as the user wrote it, without Stryker's switches (#1191)
        var annotatedBody = MutantSwitchRemover.Remove(
            LoweringAnnotations.AnnotateOriginalSpans(iteratorBody, compilation),
            semanticModel
        );

        // Stamp iteratorRegion onto the iterator parameter list and body together so parameter references inside the
        // iterator body receive the same region as their parameter local declarations.
        var stampedEnvelope = LoweringAnnotations.StampRegion(
            iteratorDecl.WithParameterList(annotatedParamList).WithBody(annotatedBody),
            iteratorRegion,
            semanticModel);
        return (stampedEnvelope.ParameterList, stampedEnvelope.Body ?? annotatedBody);
    }

    /// <summary>
    /// Adds a local per iterator parameter, initialized from the backing arguments of the iterator local that the
    /// <c>foreach</c> of <paramref name="site"/> enumerates, or from the arguments of the iterator call it enumerates.
    /// </summary>
    private static void AddParameterLocals(
        ImmutableArray<StatementSyntax>.Builder expansionStatements,
        in IteratorCallSite site,
        ParameterListSyntax stampedParamList,
        in IteratorExpansionContext context,
        ref int argumentTempCounter)
    {
        var semanticModel = context.SemanticModel;
        var compilation = semanticModel.Compilation;
        var unwrappedExpression = IteratorTypeResolver.UnwrapParentheses(site.AnnotatedForEach.Expression);
        if (unwrappedExpression is IdentifierNameSyntax localIdentifier)
        {
            var localSymbol = semanticModel.GetSymbol(localIdentifier)
                              ?? semanticModel.GetDeclaredSymbol<ISymbol>(localIdentifier);
            var localRegion = LoweringIdentity.GetRegion(localIdentifier);
            var localCppName = CppNameResolver.Resolve(
                localSymbol,
                localIdentifier.Identifier.ValueText,
                context.Dialect,
                localRegion).ToString();

            for (var i = 0; i < stampedParamList.Parameters.Count; i++)
            {
                var paramSyntax = stampedParamList.Parameters[i];
                var backingArgIdentifier = SyntaxFactory.IdentifierName($"{localCppName}_arg_{i}");
                AddParameterLocalDeclaration(expansionStatements, paramSyntax, backingArgIdentifier);
            }

            return;
        }

        var sourceInvocation = site.SourceInvocation;
        var annotatedInvocation = unwrappedExpression as InvocationExpressionSyntax
                                  ?? (compilation.ContainsSyntaxTree(sourceInvocation.SyntaxTree)
                                      ? LoweringAnnotations.AnnotateOriginalSpans(sourceInvocation, compilation)
                                      : sourceInvocation);
        EmitDirectCallParameterLocals(
            expansionStatements,
            site.IteratorMethod.Parameters,
            stampedParamList.Parameters,
            annotatedInvocation.ArgumentList.Arguments,
            ref argumentTempCounter);
    }

    // Whether the loop variable converts each element of the iterator with a .NET conversion (ForEachElementConversion)
    private static bool NeedsElementCast(
        ForEachStatementSyntax annotatedForEach,
        IMethodSymbol iteratorMethod,
        SemanticModel semanticModel) =>
        IteratorTypeResolver.IsGenericIEnumerable(iteratorMethod.ReturnType, out var iteratorElementType) &&
        ForEachElementConversion.GetTarget(annotatedForEach, iteratorElementType, semanticModel) != null;

    // The foreach being expanded (with its original spans), the iterator it enumerates and the iterator call
    private readonly record struct IteratorCallSite(
        ForEachStatementSyntax AnnotatedForEach,
        IMethodSymbol IteratorMethod,
        InvocationExpressionSyntax SourceInvocation);

    private static TypeSyntax ResolveLoopVariableType(
        ForEachStatementSyntax annotatedForEach,
        MethodDeclarationSyntax iteratorDecl,
        Compilation compilation)
    {
        if (!annotatedForEach.Type.IsVar)
        {
            return annotatedForEach.Type;
        }

        var annotatedReturnType = LoweringAnnotations.AnnotateOriginalSpans(iteratorDecl.ReturnType, compilation);
        var genericName = annotatedReturnType switch
        {
            GenericNameSyntax g => g,
            QualifiedNameSyntax { Right: GenericNameSyntax g } => g,
            AliasQualifiedNameSyntax { Name: GenericNameSyntax g } => g,
            _ => null
        };

        if (genericName is { TypeArgumentList.Arguments.Count: 1 })
        {
            return genericName.TypeArgumentList.Arguments[0].WithoutTrivia();
        }

        return annotatedForEach.Type;
    }

    /// <summary>
    /// Returns the default argument expression for an omitted optional iterator parameter, preserving original-span
    /// annotations when declared in source: the default clause, or else the argument of
    /// <c>[DefaultParameterValue(x)]</c>. Either one binds through its annotation, so <c>float.NaN</c> reaches the
    /// constant field path; an expression built from the value would be outside the compilation and bind to nothing.
    /// </summary>
    public static ExpressionSyntax GetDefaultArgumentExpression(
        IParameterSymbol parameter,
        ParameterSyntax? annotatedParamSyntax)
    {
        if (annotatedParamSyntax?.Default?.Value is { } defaultSyntax)
        {
            return defaultSyntax;
        }

        if (parameter.GetAttribute(KnownAttributes.DefaultParameterValue)?.ApplicationSyntaxReference is { } reference &&
            FindAttributeArgument(annotatedParamSyntax, reference.Span) is { } attributeArgument)
        {
            return attributeArgument;
        }

        return ArgumentMatchingHelper.CreateDefaultArgument(parameter);
    }

    // The first argument of the attribute of <parameterSyntax> whose original span is <span>
    private static ExpressionSyntax? FindAttributeArgument(ParameterSyntax? parameterSyntax, TextSpan span)
    {
        if (parameterSyntax is null)
        {
            return null;
        }

        foreach (var list in parameterSyntax.AttributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                if (HasOriginalSpan(attribute, span))
                {
                    return attribute.ArgumentList is { Arguments: [var first, ..] } ? first.Expression : null;
                }
            }
        }

        return null;
    }

    // GetAnnotations is a lazy IEnumerable, which the embedded StackLinq does not take
    private static bool HasOriginalSpan(SyntaxNode node, TextSpan span)
    {
        foreach (var annotation in node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
        {
            if (LoweringAnnotations.TryParseOriginalSpanData(annotation.Data, out var start, out var length, out _, out _) &&
                start == span.Start &&
                length == span.Length)
            {
                return true;
            }
        }

        return false;
    }

    // A local per parameter from the arguments of a direct iterator call, in parameter order; the arguments of a call
    // that names them out of order are first spilled into temporaries in the order written
    private static void EmitDirectCallParameterLocals(
        ImmutableArray<StatementSyntax>.Builder statements,
        ImmutableArray<IParameterSymbol> parameters,
        SeparatedSyntaxList<ParameterSyntax> annotatedParameters,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ref int argumentTempCounter)
    {
        var paramIndices = MapArgumentsToParameters(parameters, arguments, out var isOutOfOrder);
        var argExpressions = new ExpressionSyntax?[parameters.Length];
        if (isOutOfOrder)
        {
            SpillArguments(
                statements,
                annotatedParameters,
                arguments,
                paramIndices,
                argExpressions,
                ref argumentTempCounter
            );
        }
        else
        {
            for (var lexIdx = 0; lexIdx < arguments.Count; lexIdx++)
            {
                var paramIdx = paramIndices[lexIdx];
                if ((uint)paramIdx < (uint)argExpressions.Length)
                {
                    argExpressions[paramIdx] = arguments[lexIdx].Expression;
                }
            }
        }

        for (var i = 0; i < annotatedParameters.Count && i < parameters.Length; i++)
        {
            var paramSyntax = annotatedParameters[i];
            var initExpr = argExpressions[i] ?? GetDefaultArgumentExpression(parameters[i], paramSyntax);
            AddParameterLocalDeclaration(statements, paramSyntax, initExpr);
        }
    }

    // The parameter index of each argument in the order written (a named argument by its name, any other by its
    // position), and whether a later argument names an earlier parameter
    private static int[] MapArgumentsToParameters(
        ImmutableArray<IParameterSymbol> parameters,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        out bool isOutOfOrder)
    {
        var paramIndices = new int[arguments.Count];
        isOutOfOrder = false;
        var lastParamIdx = -1;

        for (var lexIdx = 0; lexIdx < arguments.Count; lexIdx++)
        {
            var arg = arguments[lexIdx];
            var paramIdx = arg.NameColon is { } nameColon
                ? parameters
                    .AsStackEnumerable()
                    .IndexOf(nameColon.Name.Identifier.ValueText, static (p, name) => p.Name == name)
                : lexIdx;
            if (paramIdx < 0 || paramIdx >= parameters.Length)
            {
                paramIdx = lexIdx;
            }

            if (paramIdx < lastParamIdx)
            {
                isOutOfOrder = true;
            }

            lastParamIdx = paramIdx;
            paramIndices[lexIdx] = paramIdx;
        }

        return paramIndices;
    }

    // <ParamType> __gsharp_arg_temp_<n> = <arg>; for each argument in the order written, which argExpressions then
    // passes by parameter index
    private static void SpillArguments(
        ImmutableArray<StatementSyntax>.Builder statements,
        SeparatedSyntaxList<ParameterSyntax> annotatedParameters,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        int[] paramIndices,
        ExpressionSyntax?[] argExpressions,
        ref int argumentTempCounter)
    {
        for (var lexIdx = 0; lexIdx < arguments.Count; lexIdx++)
        {
            var paramIdx = paramIndices[lexIdx];
            if ((uint)paramIdx >= (uint)annotatedParameters.Count)
            {
                continue;
            }

            var paramSyntax = annotatedParameters[paramIdx];
            var tempName = $"{GSharpInternalNames.Lowering.ArgumentTempPrefix}{argumentTempCounter++}";

            // <ParamType> __gsharp_arg_temp_<n> = <arg>;
            var tempDeclarator = SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(tempName))
                .WithInitializer(SyntaxFactory.EqualsValueClause(arguments[lexIdx].Expression));
            var tempDecl = SyntaxFactory.VariableDeclaration(
                paramSyntax.Type ?? SyntaxFactory.IdentifierName("var"),
                SyntaxFactory.SingletonSeparatedList(tempDeclarator)
            );
            statements.Add(SyntaxFactory.LocalDeclarationStatement(tempDecl));
            argExpressions[paramIdx] = SyntaxFactory.IdentifierName(tempName);
        }
    }

    // <ParamType> <param> = <init>;
    private static void AddParameterLocalDeclaration(
        ImmutableArray<StatementSyntax>.Builder statements,
        ParameterSyntax annotatedParamSyntax,
        ExpressionSyntax initializerExpression)
    {
        var declarator = SyntaxFactory.VariableDeclarator(annotatedParamSyntax.Identifier)
            .WithInitializer(SyntaxFactory.EqualsValueClause(initializerExpression))
            .WithAdditionalAnnotations(
                annotatedParamSyntax.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));
        var declaration = SyntaxFactory.VariableDeclaration(
            annotatedParamSyntax.Type ?? SyntaxFactory.IdentifierName("var"),
            SyntaxFactory.SingletonSeparatedList(declarator)
        );
        statements.Add(SyntaxFactory.LocalDeclarationStatement(declaration));
    }
}
