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

        if (node.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
        {
            context.Writer.AppendLine();
            bool isAsync = node.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword);
            string disposeMethod = isAsync ? "DisposeAsync" : "Dispose";
            foreach (var variable in node.Declaration.Variables)
            {
                string varName = variable.Identifier.Text;

                // Best-effort disposal: Since struct instance methods (like Dispose) are not transpiled by GSharp,
                // we omit the actual C++ method call to prevent undefined identifier errors.
                context.Writer.Append("    struct __GSharp_Dispose_Guard_");
                context.Writer.Append(varName);
                context.Writer.Append(" { ");
                context.Writer.Append("__device__ ~__GSharp_Dispose_Guard_");
                context.Writer.Append(varName);
                context.Writer.Append("() { /* best-effort dispose omitted */ } ");
                context.Writer.Append("} __guard_");
                context.Writer.Append(varName);
                context.Writer.Append(";");
                context.Writer.AppendLine();
            }
        }
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

        context.Writer.AppendKeyword("__shared__");
        context.Writer.AppendSpace();
        context.Writer.AppendType(typeArg);
        context.Writer.AppendSpace();
        context.Writer.Append(alloc.Declarator.Identifier.Text);
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
        var elemSize = CudaTypeMapper.GetApproximateSize(typeArg);
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
        AlignDynamicSharedMemoryOffset(typeArg, ref context.Writer);
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

        context.Writer.Append("extern __shared__ char __gsharp_dynamic_smem");
        context.Writer.AppendBrackets();
        context.Writer.AppendLine(";");
        context.Writer.AppendLine("size_t __smem_offset = 0;");
        context.HasEmittedDynamicSmemBase = true;
    }

    private static void AlignDynamicSharedMemoryOffset(ITypeSymbol typeArg, ref SourceWriter writer)
    {
        // [WHY] Computes aligned offset: (offset + align - 1) & ~(align - 1) to avoid CUDA unaligned memory access faults.
        writer.Append("__smem_offset");
        writer.AppendOperator("=");
        using (writer.Parentheses())
        {
            writer.Append("__smem_offset");
            writer.AppendOperator("+");
            writer.Append("alignof");
            using (writer.Parentheses())
            {
                writer.AppendType(typeArg);
            }

            writer.AppendOperator("-");
            writer.Append("1");
        }

        writer.AppendOperator("&");
        writer.Append("~");
        using (writer.Parentheses())
        {
            writer.Append("alignof");
            using (writer.Parentheses())
            {
                writer.AppendType(typeArg);
            }

            writer.AppendOperator("-");
            writer.Append("1");
        }

        writer.AppendSemiColon();
        writer.AppendLine();
    }

    private static void EmitDynamicSharedMemoryPointerAndAdvance(SharedMemoryAllocation alloc,
        ITypeSymbol typeArg, ref TranspileContext context)
    {
        context.Writer.AppendType(typeArg);
        context.Writer.Append("* ");
        context.Writer.Append(alloc.Declarator.Identifier.Text);
        context.Writer.AppendOperator("=");
        using (context.Writer.Parentheses())
        {
            context.Writer.AppendType(typeArg);
            context.Writer.Append("*");
        }

        using (context.Writer.Parentheses())
        {
            context.Writer.Append("__gsharp_dynamic_smem");
            context.Writer.AppendOperator("+");
            context.Writer.Append("__smem_offset");
        }

        context.Writer.AppendSemiColon();
        context.Writer.AppendLine();
        context.Writer.Append("__smem_offset");
        context.Writer.AppendOperator("+=");
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(alloc.SizeArgument);
        }

        context.Writer.AppendOperator("*");
        context.Writer.AppendKeyword("sizeof");
        using (context.Writer.Parentheses())
        {
            context.Writer.AppendType(typeArg);
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
            if (isVar)
            {
                context.Writer.AppendKeyword("auto");
            }
            else
            {
                context.Writer.AppendType(typeSymbol);
            }
            context.Writer.AppendSpace();
            context.Writer.Append(v.Identifier.Text);
            WriteDeclaratorInitializer(v, ref context);

            if (i >= variables.Count - 1)
            {
                continue;
            }

            context.Writer.AppendSemiColon();
            context.Writer.AppendLine();
        }
    }

    private static void TranspileCombinedVariableDeclarations(SeparatedSyntaxList<VariableDeclaratorSyntax> variables,
        ITypeSymbol? typeSymbol, bool isVar, bool isPointer, ref TranspileContext context)
    {
        if (isVar)
        {
            context.Writer.AppendKeyword("auto");
        }
        else
        {
            if (isPointer && typeSymbol is IPointerTypeSymbol pointerType)
            {
                context.Writer.AppendType(pointerType.PointedAtType);
            }
            else
            {
                context.Writer.AppendType(typeSymbol);
            }
        }
        context.Writer.AppendSpace();

        for (var i = 0; i < variables.Count; i++)
        {
            var v = variables[i];
            if (isPointer)
            {
                context.Writer.Append("*");
            }

            context.Writer.Append(v.Identifier.Text);
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

            context.Writer.AppendOperator("=");
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
                    context.Writer.AppendType(typeSymbol);
                }
                else
                {
                    context.Writer.AppendKeyword("auto");
                }
                context.Writer.AppendSpace();
                context.Writer.Append(singleVar.Identifier.Text);
                context.Writer.AppendSemiColon();
                context.Writer.AppendLine();
            }
        }
    }
}
