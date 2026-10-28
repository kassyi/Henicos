namespace Kassyi.GSharp.Compiler.Transpiler.Rules.DeclarationRules;

/// <summary>
/// Transpiles C# variable declaration syntax nodes into target dialect variable definitions,
/// supporting type inference (<c>auto</c>), pointer declarations, stack allocations, and multiple declarators.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and safe for concurrent execution across threads.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class VariableDeclarationTranspilerRule : SyntaxNodeTranspilerRuleBase<VariableDeclarationSyntax>
{
    private readonly struct VariableMapping(
        ITypeSymbol? typeSymbol,
        bool isVar,
        bool isPointer,
        bool shouldSplit)
    {
        public ITypeSymbol? TypeSymbol { get; } = typeSymbol;
        public bool IsVar { get; } = isVar;
        public bool IsPointer { get; } = isPointer;
        public bool ShouldSplit { get; } = shouldSplit;
    }

    /// <inheritdoc />
    public override void Transpile(VariableDeclarationSyntax node, ref TranspileContext context)
    {
        var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(node.Type);
        ValidateAndTrackVariableType(typeSymbol, node.Type, ref context);

        var mapping = AnalyzeVariableMapping(node, typeSymbol);

        if (mapping.ShouldSplit)
        {
            TranspileSplitVariableDeclarations(node.Variables, mapping.TypeSymbol, mapping.IsVar, ref context);
        }
        else
        {
            TranspileCombinedVariableDeclarations(node.Variables, mapping.TypeSymbol, mapping.IsVar, mapping.IsPointer,
                ref context);
        }
    }

    private static VariableMapping AnalyzeVariableMapping(VariableDeclarationSyntax node, ITypeSymbol? typeSymbol)
    {
        var isVar = node.Type.IsVar;
        var isPointer = typeSymbol?.TypeKind == TypeKind.Pointer;
        var shouldSplit = node.Parent is LocalDeclarationStatementSyntax or FixedStatementSyntax;

        return new VariableMapping(typeSymbol, isVar, isPointer, shouldSplit);
    }

    private static void ValidateAndTrackVariableType(ITypeSymbol? typeSymbol, TypeSyntax typeSyntax,
        ref TranspileContext context)
    {
        if (GpuCodeConstraints.IsString(typeSymbol))
        {
            context.Reporter.Fail(GSharpDiagnostics.StringNotSupported, typeSyntax);
        }

        if (GpuCodeConstraints.IsFunctionPointer(typeSymbol))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, typeSyntax, typeSyntax.Kind().ToString());
        }

        StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);
    }

    private static void TranspileSplitVariableDeclarations(SeparatedSyntaxList<VariableDeclaratorSyntax> variables,
        ITypeSymbol? typeSymbol, bool isVar, ref TranspileContext context)
    {
        var hasEmittedAny = false;
        for (var i = 0; i < variables.Count; i++)
        {
            var v = variables[i];

            if (typeSymbol != null &&
                IteratorTypeResolver.IsGenericIEnumerable(typeSymbol, out _) &&
                v.Initializer != null)
            {
                if (IteratorTypeResolver.TryResolveIteratorSource(
                        v.Initializer.Value,
                        context.Query.SemanticModel,
                        out var sourceInvocation,
                        out var iteratorMethod))
                {
                    TranspileIteratorArgumentBackingLocals(v, iteratorMethod, sourceInvocation, ref hasEmittedAny, ref context);
                    continue;
                }

                if (IteratorTypeResolver.TryResolveExternalIteratorSource(
                        v.Initializer.Value,
                        context.Query.SemanticModel,
                        out _))
                {
                    continue;
                }
            }

            if (hasEmittedAny)
            {
                context.Emitter.Writer.AppendLine(";");
            }

            if (TryTranspileStackArrayDeclaration(v, typeSymbol, ref context))
            {
                hasEmittedAny = true;
                continue;
            }

            // A split declaration has no initializer to deduce auto from, so it names the type
            var originalNode = context.Query.SemanticModel.GetOriginalNode(v);
            var splitInitialization = originalNode is not ParameterSyntax && IsJumpedOverInitialization(v, typeSymbol);
            if (isVar && !splitInitialization)
            {
                context.Emitter.Writer.Append("auto");
            }
            else if (typeSymbol != null && originalNode is ParameterSyntax paramSyntax)
            {
                var paramSymbol = context.Query.SemanticModel.GetDeclaredSymbol<IParameterSymbol>(v);
                MethodSignatureTranspiler.WriteParameterDeclaration(paramSyntax, typeSymbol, paramSymbol, ref context);
            }
            else
            {
                context.Emitter.Dialect.AppendType(ref context, typeSymbol);
            }

            context.Emitter.Writer.Append(' ');
            CppNameResolver.AppendDeclaredName(v, v.Identifier, ref context);
            if (splitInitialization)
            {
                // <type> <name>;
                // <name> = <initializer>
                context.Emitter.Writer.AppendLine(";");
                CppNameResolver.AppendDeclaredName(v, v.Identifier, ref context);
            }

            DeclarationTranspilerHelper.WriteDeclaratorInitializer(v, ref context);
            hasEmittedAny = true;
        }
    }

    // [WHY] A goto may jump past this initialized declaration (GotoJumpAnalysis); C++ forbids that jump, so the
    // declaration and the initialization become two statements. Only a type that C++ can declare without an initializer
    // and assign later is split: a primitive, an enum, a pointer or a struct that is not a ref struct. A const local is
    // emitted as a plain variable, so it is split too. A ref local, a using declaration and a ref struct local (a Span
    // that is not a stackalloc, which TryWriteFixedSizeArray handles) keep their form: a goto past them stays ill-formed
    // C++.
    private static bool IsJumpedOverInitialization(VariableDeclaratorSyntax declarator, ITypeSymbol? typeSymbol) =>
        declarator.Initializer != null &&
        IsAssignableAfterDeclaration(typeSymbol) &&
        IsJumpedOver(declarator);

    // Whether the declarator is in a plain local declaration (not ref, not using) that a goto may jump past
    private static bool IsJumpedOver(VariableDeclaratorSyntax declarator) =>
        declarator.Parent is VariableDeclarationSyntax
        {
            Type: not RefTypeSyntax,
            Parent: LocalDeclarationStatementSyntax { UsingKeyword.RawKind: (int)SyntaxKind.None } statement
        } &&
        GotoJumpAnalysis.MayBeJumpedOver(statement);

    private static bool IsAssignableAfterDeclaration(ITypeSymbol? typeSymbol) => typeSymbol switch
    {
        null or { TypeKind: TypeKind.Error } => false,
        IPointerTypeSymbol => true,
        { TypeKind: TypeKind.Enum } => true,
        { IsRefLikeType: true } => false,
        { TypeKind: TypeKind.Struct } structType when structType.IsSharedMemoryType() ||
                                                        structType.IsWmmaFragmentType() ||
                                                        structType.IsWmmaMarkerType() => false,
        { TypeKind: TypeKind.Struct } => true,
        _ => false
    };

    private static void TranspileIteratorArgumentBackingLocals(
        VariableDeclaratorSyntax declarator,
        IMethodSymbol iteratorMethod,
        InvocationExpressionSyntax sourceInvocation,
        ref bool hasEmittedAny,
        ref TranspileContext context)
    {
        if (!IteratorInliner.TryGetInlinableSyntax(
                iteratorMethod,
                context.Query.SemanticModel.Compilation,
                out var iteratorDecl))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, declarator, IteratorInliner.IteratorWithoutBody);
            return;
        }

        var parameters = iteratorMethod.Parameters;
        if (parameters.Length == 0)
        {
            return;
        }

        var localSymbol = context.Query.SemanticModel.GetDeclaredSymbol<ISymbol>(declarator);
        var localRegion = LoweringIdentity.GetRegion(declarator);
        var localCppName = CppNameResolver.Resolve(
            localSymbol,
            declarator.Identifier.ValueText,
            context.Emitter.Dialect,
            localRegion).ToString();

        var annotatedParamList = LoweringAnnotations.AnnotateOriginalSpans(
            iteratorDecl.ParameterList,
            context.Query.SemanticModel.Compilation);

        var arguments = sourceInvocation.ArgumentList.Arguments;
        var provided = new bool[parameters.Length];

        for (var lexIdx = 0; lexIdx < arguments.Count; lexIdx++)
        {
            var arg = arguments[lexIdx];
            var paramIdx = arg.NameColon is { } nameColon
                ? parameters
                    .AsStackEnumerable()
                    .IndexOf(nameColon.Name.Identifier.ValueText, static (p, name) => p.Name == name)
                : lexIdx;
            if ((uint)paramIdx >= (uint)parameters.Length)
            {
                continue;
            }

            provided[paramIdx] = true;
            EmitBackingArgumentLocal(
                localCppName,
                paramIdx,
                parameters[paramIdx],
                annotatedParamList,
                arg.Expression,
                ref hasEmittedAny,
                ref context);
        }

        for (var paramIdx = 0; paramIdx < parameters.Length; paramIdx++)
        {
            if (provided[paramIdx])
            {
                continue;
            }

            var paramSyntax = paramIdx < annotatedParamList.Parameters.Count
                ? annotatedParamList.Parameters[paramIdx]
                : null;
            var defaultExpr = IteratorInliner.GetDefaultArgumentExpression(parameters[paramIdx], paramSyntax);
            EmitBackingArgumentLocal(
                localCppName,
                paramIdx,
                parameters[paramIdx],
                annotatedParamList,
                defaultExpr,
                ref hasEmittedAny,
                ref context);
        }
    }

    private static void EmitBackingArgumentLocal(
        string localCppName,
        int paramIdx,
        IParameterSymbol paramSymbol,
        ParameterListSyntax annotatedParamList,
        ExpressionSyntax valueExpression,
        ref bool hasEmittedAny,
        ref TranspileContext context)
    {
        if (hasEmittedAny)
        {
            context.Emitter.Writer.AppendLine(";");
        }

        var paramType = paramSymbol.Type.UnwrapNullableArrayLike();
        StructTranspiler.RegisterUsedStructIfApplicable(paramType, ref context);

        if (paramIdx < annotatedParamList.Parameters.Count)
        {
            MethodSignatureTranspiler.WriteParameterDeclaration(
                annotatedParamList.Parameters[paramIdx],
                paramType,
                paramSymbol,
                ref context);
        }
        else
        {
            context.Emitter.Dialect.AppendType(ref context, paramType);
        }

        context.Emitter.Writer.Append($" {localCppName}_arg_{paramIdx} = ");
        context.Visitor.Visit(valueExpression);
        hasEmittedAny = true;
    }

    private static bool TryTranspileStackArrayDeclaration(VariableDeclaratorSyntax variable, ITypeSymbol? typeSymbol,
        ref TranspileContext context)
    {
        if (variable.Initializer == null ||
            !InitializationSyntaxExtractor.TryGetArrayElementType(typeSymbol, out var elemType, out var isConst) ||
            elemType == null)
        {
            return false;
        }

        if (GpuCodeConstraints.GetStackArrayShapeProblem(variable.Initializer.Value, typeSymbol,
                context.Query.SemanticModel) is { } problem)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, variable.Initializer.Value, problem);
            return true;
        }

        if (!InitializationSyntaxExtractor.TryGetArrayInitialization(variable.Initializer.Value, out var count,
                out var sizeExpr, out var initializerNode))
        {
            return false;
        }

        // An empty span (stackalloc T[0], []) has no storage, as in Roslyn (LocalRewriter_StackAlloc), and C++ has no
        // array of length zero: GSharpArray<T> name{}. An empty array is GSH0003 (GetStackArrayShapeProblem)
        if (count == 0 ||
            sizeExpr != null && context.Query.SemanticModel.GetSafeConstantValue(sizeExpr) is { HasValue: true, Value: 0 })
        {
            context.Emitter.Dialect.AppendType(ref context, typeSymbol);
            context.Emitter.Writer.Append(' ');
            CppNameResolver.AppendDeclaredName(variable, variable.Identifier, ref context);
            context.Emitter.Writer.Append("{}");
            return true;
        }

        if (initializerNode != null && count.HasValue)
        {
            WriteArrayWithInitializer(variable, elemType, isConst, count.Value, initializerNode, ref context);
            return true;
        }

        return TryWriteFixedSizeArray(variable, variable.Initializer.Value, elemType, isConst, sizeExpr, ref context);
    }

    private static void WriteArrayDeclarationHeader(ITypeSymbol elemType, VariableDeclaratorSyntax variable,
        bool isConst, ref TranspileContext context)
    {
        if (isConst)
        {
            context.Emitter.Writer.Append("const ");
        }

        context.Emitter.Dialect.AppendType(ref context, elemType);
        context.Emitter.Writer.Append(' ');
        CppNameResolver.AppendDeclaredName(variable, variable.Identifier, ref context);
    }

    private static void WriteArrayWithInitializer(VariableDeclaratorSyntax variable, ITypeSymbol elemType, bool isConst,
        int count, SyntaxNode initializerNode, ref TranspileContext context)
    {
        WriteArrayDeclarationHeader(elemType, variable, isConst, ref context);
        using (context.Emitter.Writer.Brackets())
        {
            context.Emitter.Writer.Append(count);
        }

        context.Emitter.Writer.Append("=");
        context.Visitor.Visit(initializerNode);
    }

    // Span<int> s = stackalloc int[4];          ->  int s[4]{};
    // the same under [SkipLocalsInit]            ->  int s[4];
    // ReadOnlySpan<int> r = stackalloc int[4];  ->  const int r[4]{};  (a const array needs an initializer)
    // int[] a = new int[4];                     ->  int a[4]{};        (a C# array is zeroed whatever the attribute says)
    private static bool TryWriteFixedSizeArray(VariableDeclaratorSyntax variable, ExpressionSyntax creation,
        ITypeSymbol elemType, bool isConst, ExpressionSyntax? sizeExpr, ref TranspileContext context)
    {
        if (sizeExpr == null)
        {
            return false;
        }

        if (!GpuCodeConstraints.IsConstantArraySize(sizeExpr, context.Query.SemanticModel))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, sizeExpr, GpuCodeConstraints.DynamicArraySize);
            return true;
        }

        WriteArrayDeclarationHeader(elemType, variable, isConst, ref context);
        using (context.Emitter.Writer.Brackets())
        {
            context.Visitor.Visit(sizeExpr);
        }

        var zeroed = creation is not StackAllocArrayCreationExpressionSyntax ||
                     isConst ||
                     context.Query.SemanticModel.AreLocalsZeroedAt(creation);
        if (zeroed && !isConst && IsJumpedOver(variable))
        {
            // [WHY] A goto may jump past the declaration, and C++ forbids jumping past its {} initialization. The zero
            // fill becomes a statement at the same place, so the array is zeroed again whenever control passes there,
            // as in C#. A const array (ReadOnlySpan<T> = stackalloc) needs its initializer and keeps its form.
            // <type> <name>[<size>];
            // memset(<name>, 0, sizeof(<name>))
            context.Emitter.Writer.AppendLine(";");
            context.Emitter.Writer.Append("memset");
            using (context.Emitter.Writer.Parentheses())
            {
                CppNameResolver.AppendDeclaredName(variable, variable.Identifier, ref context);
                context.Emitter.Writer.Append(",0,sizeof");
                using (context.Emitter.Writer.Parentheses())
                {
                    CppNameResolver.AppendDeclaredName(variable, variable.Identifier, ref context);
                }
            }

            return true;
        }

        context.Emitter.Writer.AppendIf(zeroed, "{}");
        return true;
    }

    private static void TranspileCombinedVariableDeclarations(SeparatedSyntaxList<VariableDeclaratorSyntax> variables,
        ITypeSymbol? typeSymbol, bool isVar, bool isPointer, ref TranspileContext context) =>
        DeclarationTranspilerHelper.TranspileCombinedDeclarations(variables, typeSymbol, isVar, isPointer, ref context);
}
