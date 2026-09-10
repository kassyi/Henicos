namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles C# variable declarations and static/dynamic shared memory allocations to CUDA C++.</summary>
/// <remarks><para>Implements: <b>SYN-03, SYN-18, SYN-20, SYN-23, SYN-24, SYN-30, SYN-33, SYN-74, SYN-79</b></para></remarks>
internal static class DeclarationTranspilers
{
    private readonly struct SharedMemoryAllocation(
        VariableDeclaratorSyntax declarator,
        ExpressionSyntax sizeArgument,
        int? constantSize)
    {
        public VariableDeclaratorSyntax Declarator { get; } = declarator;
        public ExpressionSyntax SizeArgument { get; } = sizeArgument;
        public int? ConstantSize { get; } = constantSize;
        public bool IsDynamic => !ConstantSize.HasValue;
        public int StaticSize => ConstantSize ?? 0;
    }

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

    public static void TranspileLocalDeclarationStatement(LocalDeclarationStatementSyntax node,
        ref TranspileContext context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node.Declaration.Type).Type;
        if (typeSymbol?.Name == TypeConstants.SharedMemoryType &&
            typeSymbol.ContainingNamespace.IsCoreNamespace())
        {
            ProcessSharedMemoryDeclaration(node, (INamedTypeSymbol)typeSymbol, ref context);
            return;
        }

        HoistDeclarationExpressions(node, ref context);
        context.Visitor.VisitVariableDeclaration(node.Declaration);
        context.Writer.AppendSemiColon();
    }

    private static void ProcessSharedMemoryDeclaration(LocalDeclarationStatementSyntax node,
        INamedTypeSymbol typeSymbol, ref TranspileContext context)
    {
        var typeArg = typeSymbol.TypeArguments[0];

        for (var i = 0; i < node.Declaration.Variables.Count; i++)
        {
            var alloc = AnalyzeSharedMemoryAllocation(node.Declaration.Variables[i], context.SemanticModel);
            if (alloc == null)
            {
                continue;
            }

            if (alloc.Value.IsDynamic)
            {
                WriteDynamicSharedMemory(alloc.Value, typeArg, node, i, ref context);
            }
            else
            {
                WriteStaticSharedMemory(alloc.Value, typeArg, node, i, ref context);
            }
        }
    }

    private static SharedMemoryAllocation? AnalyzeSharedMemoryAllocation(VariableDeclaratorSyntax declarator,
        SemanticModel semanticModel)
    {
        ExpressionSyntax? sizeArg = null;

        if (declarator.Initializer?.Value is ObjectCreationExpressionSyntax objCreation)
        {
            sizeArg = objCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        }
        else if (declarator.Initializer?.Value is ImplicitObjectCreationExpressionSyntax implCreation)
        {
            sizeArg = implCreation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        }

        if (sizeArg == null)
        {
            return null;
        }

        var constantValue = semanticModel.GetConstantValue(sizeArg);
        return new SharedMemoryAllocation(
            declarator,
            sizeArg,
            constantValue.HasValue ? Convert.ToInt32(constantValue.Value!) : null
        );
    }

    private static void WriteStaticSharedMemory(SharedMemoryAllocation alloc,
        ITypeSymbol typeArg, LocalDeclarationStatementSyntax node, int i, ref TranspileContext context)
    {
        CheckExcessiveSharedMemoryUsage(alloc.SizeArgument, alloc.StaticSize, typeArg, ref context);

        context.Writer.Append("__shared__ ");
        context.AppendType(typeArg);
        context.Writer.Append($" {alloc.Declarator.Identifier.Text}");
        using (context.Writer.Brackets())
        {
            context.Visitor.Visit(alloc.SizeArgument);
        }

        context.Writer.Append(";");
        if (i < node.Declaration.Variables.Count - 1)
        {
            context.Writer.AppendLine();
        }
    }

    private static void CheckExcessiveSharedMemoryUsage(ExpressionSyntax sizeArg, int elementCount,
        ITypeSymbol typeArg, ref TranspileContext context)
    {
        var elemSize = context.Dialect.GetApproximateSize(typeArg);
        var byteSize = elementCount * elemSize;
        if (byteSize > 32768)
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ExcessiveSharedMemoryWarning,
                sizeArg.GetLocation(), byteSize, 32768));
        }
    }

    private static void WriteDynamicSharedMemory(SharedMemoryAllocation alloc, ITypeSymbol typeArg,
        LocalDeclarationStatementSyntax node, int index, ref TranspileContext context)
    {
        EnsureDynamicSharedMemoryBase(ref context);
        AlignDynamicSharedMemoryOffset(typeArg, ref context);
        EmitDynamicSharedMemoryPointerAndAdvance(alloc, typeArg, ref context);

        if (index < node.Declaration.Variables.Count - 1)
        {
            context.Writer.AppendLine();
        }
    }

    private static void EnsureDynamicSharedMemoryBase(ref TranspileContext context)
    {
        if (context.HasEmittedDynamicSmemBase)
        {
            return;
        }

        context.Writer.AppendLine("extern __shared__ char __gsharp_dynamic_smem[];");
        context.Writer.AppendLine("size_t __smem_offset = 0;");
        context.HasEmittedDynamicSmemBase = true;
    }

    private static void AlignDynamicSharedMemoryOffset(ITypeSymbol typeArg, ref TranspileContext context)
    {
        // [WHY] Computes aligned offset: (offset + align - 1) & ~(align - 1) to avoid CUDA unaligned memory access faults.
        context.Writer.Append("__smem_offset = ");
        using (context.Writer.Parentheses())
        {
            context.Writer.Append("__smem_offset + alignof");
            using (context.Writer.Parentheses())
            {
                context.AppendType(typeArg);
            }
            context.Writer.Append(" - 1");
        }

        context.Writer.Append(" & ~");
        using (context.Writer.Parentheses())
        {
            context.Writer.Append("alignof");
            using (context.Writer.Parentheses())
            {
                context.AppendType(typeArg);
            }
            context.Writer.Append(" - 1");
        }

        context.Writer.AppendSemiColonLine();
    }

    private static void EmitDynamicSharedMemoryPointerAndAdvance(SharedMemoryAllocation alloc,
        ITypeSymbol typeArg, ref TranspileContext context)
    {
        context.AppendType(typeArg);
        context.Writer.Append($"* {alloc.Declarator.Identifier.Text} = ");
        using (context.Writer.Parentheses())
        {
            context.AppendType(typeArg);
            context.Writer.Append("*");
        }

        context.Writer.Append("(__gsharp_dynamic_smem + __smem_offset)");

        context.Writer.AppendLine(";");
        context.Writer.Append("__smem_offset += ");
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(alloc.SizeArgument);
        }

        context.Writer.Append(" * sizeof");
        using (context.Writer.Parentheses())
        {
            context.AppendType(typeArg);
        }
        context.Writer.AppendSemiColon();
    }

    public static void TranspileVariableDeclaration(VariableDeclarationSyntax node, ref TranspileContext context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node.Type).Type;
        ValidateAndTrackVariableType(typeSymbol, node.Type, ref context);

        var mapping = AnalyzeVariableMapping(node, typeSymbol);

        if (mapping.ShouldSplit)
        {
            TranspileSplitVariableDeclarations(node.Variables, mapping.TypeSymbol, mapping.IsVar, ref context);
        }
        else
        {
            TranspileCombinedVariableDeclarations(node.Variables, mapping.TypeSymbol, mapping.IsVar, mapping.IsPointer, ref context);
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
        if (typeSymbol is { SpecialType: SpecialType.System_String })
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.StringNotSupported,
                typeSyntax.GetLocation()));
        }

        StructTranspiler.RegisterUsedStructIfApplicable(typeSymbol, ref context);
    }

    private static void TranspileSplitVariableDeclarations(SeparatedSyntaxList<VariableDeclaratorSyntax> variables,
        ITypeSymbol? typeSymbol, bool isVar, ref TranspileContext context)
    {
        for (var i = 0; i < variables.Count; i++)
        {
            var v = variables[i];

            if (TryTranspileStackArrayDeclaration(v, typeSymbol, ref context))
            {
                if (i < variables.Count - 1)
                {
                    context.Writer.AppendSemiColonLine();
                }
                continue;
            }

            if (isVar)
            {
                context.Writer.Append($"auto {v.Identifier.Text}");
            }
            else
            {
                context.AppendType(typeSymbol);
                context.Writer.Append($" {v.Identifier.Text}");
            }
            WriteDeclaratorInitializer(v, ref context);

            if (i >= variables.Count - 1)
            {
                continue;
            }

            context.Writer.AppendSemiColonLine();
        }
    }

    private static bool TryTranspileStackArrayDeclaration(VariableDeclaratorSyntax variable, ITypeSymbol? typeSymbol, ref TranspileContext context)
    {
        if (variable.Initializer == null)
        {
            return false;
        }

        var initVal = variable.Initializer.Value;

        ITypeSymbol? elemType = null;
        var isConst = false;

        if (typeSymbol is INamedTypeSymbol { TypeArguments.Length: 1, Name: "Span" or "ReadOnlySpan" } named)
        {
            elemType = named.TypeArguments[0];
            isConst = named.Name == "ReadOnlySpan";
        }
        else if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            elemType = arrayType.ElementType;
        }

        if (elemType == null)
        {
            return false;
        }

        // Case 1: CollectionExpression [10, 20, 30]
        if (initVal is CollectionExpressionSyntax collectionExpr)
        {
            if (isConst)
            {
                context.Writer.Append("const ");
            }

            context.AppendType(elemType);
            context.Writer.Append($" {variable.Identifier.Text}");
            using (context.Writer.Brackets())
            {
                context.Writer.Append(collectionExpr.Elements.Count);
            }
            context.Writer.AppendOperator("=");
            context.Visitor.Visit(collectionExpr);
            return true;
        }

        // Case 2: StackAllocArrayCreationExpression stackalloc int[4] or stackalloc int[] { 1, 2, 3 }
        if (initVal is StackAllocArrayCreationExpressionSyntax stackAlloc)
        {
            if (stackAlloc.Initializer != null)
            {
                if (isConst)
                {
                    context.Writer.Append("const ");
                }

                context.AppendType(elemType);
                context.Writer.Append($" {variable.Identifier.Text}");
                using (context.Writer.Brackets())
                {
                    context.Writer.Append(stackAlloc.Initializer.Expressions.Count);
                }
                context.Writer.AppendOperator("=");
                context.Visitor.Visit(stackAlloc.Initializer);
                return true;
            }

            if (stackAlloc.Type is ArrayTypeSyntax { RankSpecifiers.Count: > 0 } arrayTypeSyntax)
            {
                var sizeExpr = arrayTypeSyntax.RankSpecifiers[0].Sizes.FirstOrDefault();
                if (sizeExpr != null)
                {
                    var constVal = context.SemanticModel.GetConstantValue(sizeExpr);
                    if (!constVal.HasValue)
                    {
                        context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                            GSharpDiagnostics.UnsupportedSyntax,
                            sizeExpr.GetLocation(),
                            "dynamic stackalloc size"));
                        return true;
                    }

                    context.AppendType(elemType);
                    context.Writer.Append($" {variable.Identifier.Text}");
                    using (context.Writer.Brackets())
                    {
                        context.Visitor.Visit(sizeExpr);
                    }
                    return true;
                }
            }
        }

        // Case 3: ImplicitStackAllocArrayCreationExpression stackalloc[] { 1, 2, 3 }
        if (initVal is ImplicitStackAllocArrayCreationExpressionSyntax implStackAlloc)
        {
            if (isConst)
            {
                context.Writer.Append("const ");
            }

            context.AppendType(elemType);
            context.Writer.Append($" {variable.Identifier.Text}");
            using (context.Writer.Brackets())
            {
                context.Writer.Append(implStackAlloc.Initializer.Expressions.Count);
            }
            context.Writer.Append(" = ");
            context.Visitor.Visit(implStackAlloc.Initializer);
            return true;
        }

        // Case 4: ArrayCreationExpression new int[] { 1, 2, 3 } or new int[4]
        if (initVal is ArrayCreationExpressionSyntax arrayCreation)
        {
            if (arrayCreation.Initializer != null)
            {
                if (isConst)
                {
                    context.Writer.Append("const ");
                }

                context.AppendType(elemType);
                context.Writer.Append($" {variable.Identifier.Text}");
                using (context.Writer.Brackets())
                {
                    context.Writer.Append(arrayCreation.Initializer.Expressions.Count);
                }
                context.Writer.AppendOperator("=");
                context.Visitor.Visit(arrayCreation.Initializer);
                return true;
            }

            if (arrayCreation.Type.RankSpecifiers.Count > 0)
            {
                var sizeExpr = arrayCreation.Type.RankSpecifiers[0].Sizes.FirstOrDefault();
                if (sizeExpr != null)
                {
                    var constVal = context.SemanticModel.GetConstantValue(sizeExpr);
                    if (!constVal.HasValue)
                    {
                        context.ReportDiagnostic?.Invoke(Diagnostic.Create(
                            GSharpDiagnostics.UnsupportedSyntax,
                            sizeExpr.GetLocation(),
                            "dynamic array size"));
                        return true;
                    }

                    context.AppendType(elemType);
                    context.Writer.Append($" {variable.Identifier.Text}");
                    using (context.Writer.Brackets())
                    {
                        context.Visitor.Visit(sizeExpr);
                    }
                    return true;
                }
            }
        }

        return false;
    }

    private static void TranspileCombinedVariableDeclarations(SeparatedSyntaxList<VariableDeclaratorSyntax> variables,
        ITypeSymbol? typeSymbol, bool isVar, bool isPointer, ref TranspileContext context)
    {
        if (isVar)
        {
            context.Writer.Append("auto");
        }
        else
        {
            if (isPointer && typeSymbol is IPointerTypeSymbol pointerType)
            {
                context.AppendType(pointerType.PointedAtType);
            }
            else
            {
                context.AppendType(typeSymbol);
            }
        }
        context.Writer.Append(" ");

        for (var i = 0; i < variables.Count; i++)
        {
            var v = variables[i];
            if (isPointer)
            {
                context.Writer.Append($"*{v.Identifier.Text}");
            }
            else
            {
                context.Writer.Append(v.Identifier.Text);
            }

            WriteDeclaratorInitializer(v, ref context);

            if (i < variables.Count - 1)
            {
                context.Writer.AppendComma();
            }
        }
    }

    private static void WriteDeclaratorInitializer(VariableDeclaratorSyntax variable, ref TranspileContext context)
    {
        if (variable.Initializer != null)
        {
            if (context.SkipLocalsInit)
            {
                var val = variable.Initializer.Value;
                if (val is ImplicitObjectCreationExpressionSyntax or ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: 0 } or DefaultExpressionSyntax ||
                    val is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.DefaultLiteralExpression))
                {
                    return; // Skip zero-initialization
                }
            }

            context.Writer.Append(" = ");
            context.Visitor.Visit(variable.Initializer.Value);
        }
    }

    public static void HoistDeclarationExpressions(SyntaxNode node, ref TranspileContext context)
    {
        foreach (var desc in node.DescendantNodes(n => n == node || n is not BlockSyntax))
        {
            if (desc is DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax singleVar } declExpr)
            {
                var typeSymbol = context.SemanticModel.GetTypeInfo(declExpr).Type;
                if (typeSymbol != null)
                {
                    context.AppendType(typeSymbol);
                }
                else
                {
                    context.Writer.Append("auto");
                }
                context.Writer.AppendLine($" {singleVar.Identifier.Text};");
            }
        }
    }
}
