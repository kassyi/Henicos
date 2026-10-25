namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Expands a <c>foreach</c> statement over a <c>[GpuDeviceFunction]</c> iterator into an inline scoped block where each
/// <c>yield return</c> is replaced by the consumer body and <c>yield break</c> / consumer <c>break</c> / <c>continue</c>
/// become structured forward <c>goto</c> statements.
/// </summary>
internal static class IteratorInliner
{
    /// <summary>Maximum allowed nesting depth of inlined iterator expansions before failing transpilation.</summary>
    public const int MaxInlineDepth = 16;

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
    /// Expands <paramref name="forEachNode"/> over <paramref name="iteratorMethod"/> into a two-statement block consisting
    /// of the scoped expansion block and the trailing <c>__gsharp_iter_end_&lt;r&gt;:;</c> label.
    /// </summary>
    public static BlockSyntax Expand(
        ForEachStatementSyntax forEachNode,
        IMethodSymbol iteratorMethod,
        InvocationExpressionSyntax sourceInvocation,
        SemanticModel semanticModel,
        ref int regionCounter,
        ref int argumentTempCounter,
        ITranspilerDialect dialect,
        Reporter reporter)
    {
        var compilation = semanticModel.Compilation;
        if (!TryGetInlinableSyntax(iteratorMethod, compilation, out var iteratorDecl) || iteratorDecl.Body is not { } iteratorBody)
        {
            reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, forEachNode, IteratorWithoutBody);
            return SyntaxFactory.Block();
        }

        var annotatedForEach = compilation.ContainsSyntaxTree(forEachNode.SyntaxTree)
            ? LoweringAnnotations.AnnotateOriginalSpans(forEachNode, compilation)
            : forEachNode;
        var annotatedParamList = LoweringAnnotations.AnnotateOriginalSpans(iteratorDecl.ParameterList, compilation);
        var annotatedBody = LoweringAnnotations.AnnotateOriginalSpans(iteratorBody, compilation);
        var iteratorRegion = ++regionCounter;

        // Stamp iteratorRegion onto the iterator parameter list and body together so parameter references inside the
        // iterator body receive the same region as their parameter local declarations.
        var stampedEnvelope = LoweringAnnotations.StampRegion(
            iteratorDecl.WithParameterList(annotatedParamList).WithBody(annotatedBody),
            iteratorRegion,
            semanticModel);
        var stampedParamList = stampedEnvelope.ParameterList;
        var stampedBody = stampedEnvelope.Body ?? annotatedBody;

        var loopVariableType = ResolveLoopVariableType(annotatedForEach, iteratorDecl, compilation);

        var expansionStatements = ImmutableArray.CreateBuilder<StatementSyntax>();

        var unwrappedExpression = IteratorTypeResolver.UnwrapParentheses(annotatedForEach.Expression);
        if (unwrappedExpression is IdentifierNameSyntax localIdentifier)
        {
            var localSymbol = semanticModel.GetSymbol(localIdentifier)
                              ?? semanticModel.GetDeclaredSymbol<ISymbol>(localIdentifier);
            var localRegion = LoweringIdentity.GetRegion(localIdentifier);
            var localCppName = CppNameResolver.Resolve(
                localSymbol,
                localIdentifier.Identifier.ValueText,
                dialect,
                localRegion).ToString();

            for (var i = 0; i < stampedParamList.Parameters.Count; i++)
            {
                var paramSyntax = stampedParamList.Parameters[i];
                var backingArgIdentifier = SyntaxFactory.IdentifierName($"{localCppName}_arg_{i}");
                AddParameterLocalDeclaration(expansionStatements, paramSyntax, backingArgIdentifier);
            }
        }
        else
        {
            var annotatedInvocation = unwrappedExpression as InvocationExpressionSyntax
                                      ?? (compilation.ContainsSyntaxTree(sourceInvocation.SyntaxTree)
                                          ? LoweringAnnotations.AnnotateOriginalSpans(sourceInvocation, compilation)
                                          : sourceInvocation);
            EmitDirectCallParameterLocals(
                expansionStatements,
                iteratorMethod.Parameters,
                stampedParamList.Parameters,
                annotatedInvocation.ArgumentList.Arguments,
                ref argumentTempCounter);
        }

        var endLabel = $"{GSharpInternalNames.Iterator.EndLabelPrefix}{iteratorRegion}";
        var yieldRewriter = new YieldRewriter(
            annotatedForEach,
            loopVariableType,
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
    /// annotations when declared in source.
    /// </summary>
    public static ExpressionSyntax GetDefaultArgumentExpression(
        IParameterSymbol parameter,
        ParameterSyntax? annotatedParamSyntax)
    {
        if (annotatedParamSyntax?.Default?.Value is { } defaultSyntax)
        {
            return defaultSyntax;
        }

        return ArgumentMatchingHelper.CreateDefaultArgument(parameter);
    }

    private static void EmitDirectCallParameterLocals(
        ImmutableArray<StatementSyntax>.Builder statements,
        ImmutableArray<IParameterSymbol> parameters,
        SeparatedSyntaxList<ParameterSyntax> annotatedParameters,
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        ref int argumentTempCounter)
    {
        var paramIndices = new int[arguments.Count];
        var isOutOfOrder = false;
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

        var argExpressions = new ExpressionSyntax?[parameters.Length];
        if (isOutOfOrder)
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

    private sealed class YieldRewriter(
        ForEachStatementSyntax annotatedForEach,
        TypeSyntax loopVariableType,
        int iteratorRegion,
        string endLabel,
        SemanticModel semanticModel) : CSharpSyntaxRewriter
    {
        private int _yieldCounter;
        private int _regionCounter;

        public BlockSyntax Rewrite(BlockSyntax body, ref int regionCounter)
        {
            _regionCounter = regionCounter;
            var result = Visit(body) as BlockSyntax ?? body;
            regionCounter = _regionCounter;
            return result;
        }

        public override SyntaxNode? VisitYieldStatement(YieldStatementSyntax node)
        {
            if (node.IsKind(SyntaxKind.YieldBreakStatement))
            {
                // goto __gsharp_iter_end_<r>;
                return SyntaxFactory.GotoStatement(
                        SyntaxKind.GotoStatement,
                        SyntaxFactory.IdentifierName(endLabel)
                    )
                    .WithAdditionalAnnotations(node.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));
            }

            var yieldIndex = _yieldCounter++;
            var consumerRegion = ++_regionCounter;
            var nextLabel = $"{GSharpInternalNames.Iterator.NextLabelPrefix}{iteratorRegion}_{yieldIndex}";
            var yieldedExpression = Visit(node.Expression) as ExpressionSyntax
                                    ?? SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);

            // Stamp consumerRegion onto the uninitialized loop variable declaration and consumer body together so that
            // references to the foreach loop variable and consumer-local declarations receive consumerRegion without
            // overwriting iteratorRegion on yieldedExpression.
            // { <ElementType> <loopVar>; { <consumerBody> } }
            var uninitDeclarator = SyntaxFactory.VariableDeclarator(annotatedForEach.Identifier)
                .WithAdditionalAnnotations(
                    annotatedForEach.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind));
            var uninitVarDecl = SyntaxFactory.VariableDeclaration(
                loopVariableType,
                SyntaxFactory.SingletonSeparatedList(uninitDeclarator)
            );
            var uninitLocalDecl = SyntaxFactory.LocalDeclarationStatement(uninitVarDecl);

            var rewrittenConsumerBody = BranchTargetRewriter.Rewrite(annotatedForEach.Statement, endLabel, nextLabel);
            var rawConsumerBlock = (rewrittenConsumerBody is BlockSyntax block
                    ? SyntaxFactory.Block(block.Statements)
                    : SyntaxFactory.Block(rewrittenConsumerBody))
                .WithAdditionalAnnotations(new SyntaxAnnotation(LoweringAnnotations.ScopedBlockAnnotationKind));

            var stampedConsumerEnvelope = LoweringAnnotations.StampRegion(
                SyntaxFactory.Block(uninitLocalDecl, rawConsumerBlock),
                consumerRegion,
                semanticModel);

            // { <ElementType> <loopVar> = <yieldedExpr>; { <consumerBody> } __gsharp_iter_next_<r>_<j>: ; }
            var stampedDeclStmt = (LocalDeclarationStatementSyntax)stampedConsumerEnvelope.Statements[0];
            var stampedDeclarator = stampedDeclStmt.Declaration.Variables[0]
                .WithInitializer(SyntaxFactory.EqualsValueClause(yieldedExpression));
            var loopVarDeclaration = stampedDeclStmt.WithDeclaration(
                stampedDeclStmt.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(stampedDeclarator)));
            var consumerBodyBlock = stampedConsumerEnvelope.Statements[1];

            var nextLabeledStmt = SyntaxFactory.LabeledStatement(
                SyntaxFactory.Identifier(nextLabel),
                SyntaxFactory.EmptyStatement()
            );

            return SyntaxFactory.Block(loopVarDeclaration, consumerBodyBlock, nextLabeledStmt)
                .WithAdditionalAnnotations(new SyntaxAnnotation(LoweringAnnotations.ScopedBlockAnnotationKind));
        }

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => node;

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => node;

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => node;

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node;
    }
}
