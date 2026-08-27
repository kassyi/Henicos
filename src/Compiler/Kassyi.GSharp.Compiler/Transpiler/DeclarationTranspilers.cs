namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Transpiles C# variable declarations and static/dynamic shared memory allocations to CUDA C++.</summary>
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
        string cppType,
        string baseCppType,
        bool isPointer,
        bool shouldSplitStatements)
    {
        public string CppType { get; } = cppType;
        public string BaseCppType { get; } = baseCppType;
        public bool IsPointer { get; } = isPointer;
        public bool ShouldSplitStatements { get; } = shouldSplitStatements;
    }

    public static void TranspileLocalDeclarationStatement(LocalDeclarationStatementSyntax node,
        ref TranspileContext context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node.Declaration.Type).Type;
        if (typeSymbol?.Name == TypeConstants.SharedMemoryType &&
            typeSymbol.ContainingNamespace.ToDisplayString() == TypeConstants.CoreNamespace)
        {
            ProcessSharedMemoryDeclaration(node, (INamedTypeSymbol)typeSymbol, ref context);
            return;
        }

        context.Visitor.VisitVariableDeclaration(node.Declaration);
        context.Writer.AppendSemiColon();
    }

    private static void ProcessSharedMemoryDeclaration(LocalDeclarationStatementSyntax node,
        INamedTypeSymbol typeSymbol, ref TranspileContext context)
    {
        var typeArg = typeSymbol.TypeArguments[0];
        var cppElementType = CudaTypeMapper.MapType(typeArg);

        for (var i = 0; i < node.Declaration.Variables.Count; i++)
        {
            var alloc = AnalyzeSharedMemoryAllocation(node.Declaration.Variables[i], context.SemanticModel);
            if (alloc == null)
            {
                continue;
            }

            if (alloc.Value.IsDynamic)
            {
                WriteDynamicSharedMemory(alloc.Value, cppElementType, node, i, ref context);
            }
            else
            {
                WriteStaticSharedMemory(alloc.Value, cppElementType, typeArg, node, i, ref context);
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
            sizeArg = implCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
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

    private static void WriteStaticSharedMemory(SharedMemoryAllocation alloc, string cppElementType,
        ITypeSymbol typeArg, LocalDeclarationStatementSyntax node, int i, ref TranspileContext context)
    {
        CheckExcessiveSharedMemoryUsage(alloc.SizeArgument, alloc.StaticSize, typeArg, ref context);

        context.Writer.AppendKeyword("__shared__");
        context.Writer.AppendSpace();
        context.Writer.Append(cppElementType);
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

    private static void WriteDynamicSharedMemory(SharedMemoryAllocation alloc, string cppElementType,
        LocalDeclarationStatementSyntax node, int index, ref TranspileContext context)
    {
        EnsureDynamicSharedMemoryBase(ref context);
        AlignDynamicSharedMemoryOffset(cppElementType, ref context.Writer);
        EmitDynamicSharedMemoryPointerAndAdvance(alloc, cppElementType, ref context);

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

    private static void AlignDynamicSharedMemoryOffset(string cppElementType, ref SourceWriter writer)
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
                writer.Append(cppElementType);
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
                writer.Append(cppElementType);
            }

            writer.AppendOperator("-");
            writer.Append("1");
        }

        writer.AppendSemiColon();
        writer.AppendLine();
    }

    private static void EmitDynamicSharedMemoryPointerAndAdvance(SharedMemoryAllocation alloc,
        string cppElementType, ref TranspileContext context)
    {
        context.Writer.Append($"{cppElementType}* {alloc.Declarator.Identifier.Text}");
        context.Writer.AppendOperator("=");
        using (context.Writer.Parentheses())
        {
            context.Writer.Append($"{cppElementType}*");
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
        context.Writer.Append($"sizeof({cppElementType})");
        context.Writer.AppendSemiColon();
    }

    public static void TranspileVariableDeclaration(VariableDeclarationSyntax node, ref TranspileContext context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node.Type).Type;
        ValidateAndTrackVariableType(typeSymbol, node.Type, ref context);

        var mapping = AnalyzeVariableMapping(node, typeSymbol);

        if (mapping.ShouldSplitStatements)
        {
            TranspileSplitVariableDeclarations(node.Variables, mapping.CppType, ref context);
        }
        else
        {
            TranspileCombinedVariableDeclarations(node.Variables, mapping.BaseCppType, mapping.IsPointer, ref context);
        }
    }

    private static VariableMapping AnalyzeVariableMapping(VariableDeclarationSyntax node, ITypeSymbol? typeSymbol)
    {
        var cppType = node.Type.IsVar ? "auto" : CudaTypeMapper.MapType(typeSymbol);
        var isPointer = typeSymbol?.TypeKind == TypeKind.Pointer;
        var baseCppType = isPointer ? cppType.TrimEnd('*').TrimEnd() : cppType;
        var shouldSplit = node.Parent is LocalDeclarationStatementSyntax || node.Parent is FixedStatementSyntax;

        return new VariableMapping(cppType, baseCppType, isPointer, shouldSplit);
    }

    private static void ValidateAndTrackVariableType(ITypeSymbol? typeSymbol, TypeSyntax typeSyntax,
        ref TranspileContext context)
    {
        if (typeSymbol is { SpecialType: SpecialType.System_String })
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.StringNotSupported,
                typeSyntax.GetLocation()));
        }

        if (typeSymbol is { TypeKind: TypeKind.Struct } &&
            typeSymbol.Name != "GpuBigIntRef" &&
            typeSymbol.ContainingNamespace.ToDisplayString() != TypeConstants.SystemNamespace)
        {
            context.UsedStructs.Add((INamedTypeSymbol)typeSymbol);
        }
    }

    private static void TranspileSplitVariableDeclarations(SeparatedSyntaxList<VariableDeclaratorSyntax> variables,
        string cppType, ref TranspileContext context)
    {
        for (var i = 0; i < variables.Count; i++)
        {
            var v = variables[i];
            context.Writer.Append(cppType);
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
        string baseCppType, bool isPointer, ref TranspileContext context)
    {
        context.Writer.Append(baseCppType);
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
            context.Writer.AppendOperator("=");
            context.Visitor.Visit(variable.Initializer.Value);
        }
    }
}
