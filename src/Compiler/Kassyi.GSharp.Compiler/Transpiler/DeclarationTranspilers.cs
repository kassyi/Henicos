namespace Kassyi.GSharp.Compiler.Transpiler;

internal static class DeclarationTranspilers
{
    public static void TranspileLocalDeclarationStatement(LocalDeclarationStatementSyntax node, RoslynToCudaVisitor context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node.Declaration.Type).Type;
        if (typeSymbol?.Name == TypeConstants.SharedMemoryType &&
            typeSymbol.ContainingNamespace.ToDisplayString() == TypeConstants.CoreNamespace)
        {
            ProcessSharedMemoryDeclaration(node, (INamedTypeSymbol)typeSymbol, context);
            return;
        }

        context.VisitVariableDeclaration(node.Declaration);
        context.Writer.AppendSemiColon();
    }

    private static void ProcessSharedMemoryDeclaration(LocalDeclarationStatementSyntax node, INamedTypeSymbol typeSymbol, RoslynToCudaVisitor context)
    {
        var typeArg = typeSymbol.TypeArguments[0];
        var cppElementType = CudaTypeMapper.MapType(typeArg);

        for (var i = 0; i < node.Declaration.Variables.Count; i++)
        {
            var v = node.Declaration.Variables[i];
            var sizeArg = (v.Initializer?.Value as ObjectCreationExpressionSyntax)?.ArgumentList?.Arguments
                .FirstOrDefault()?.Expression;
            if (sizeArg == null && v.Initializer?.Value is ImplicitObjectCreationExpressionSyntax implCreation)
            {
                sizeArg = implCreation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            }

            if (sizeArg == null)
            {
                continue;
            }

            var constantValue = context.SemanticModel.GetConstantValue(sizeArg);
            if (constantValue.HasValue)
            {
                WriteStaticSharedMemory(v, sizeArg, constantValue.Value!, cppElementType, typeArg, node, i, context);
            }
            else
            {
                WriteDynamicSharedMemory(v, sizeArg, cppElementType, node, i, context);
            }
        }
    }

    private static void WriteStaticSharedMemory(VariableDeclaratorSyntax v, ExpressionSyntax sizeArg, object constantValue,
        string cppElementType, ITypeSymbol typeArg, LocalDeclarationStatementSyntax node, int i, RoslynToCudaVisitor context)
    {
        var elemSize = CudaTypeMapper.GetApproximateSize(typeArg);
        var byteSize = (int)constantValue * elemSize;
        if (byteSize > 32768)
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ExcessiveSharedMemoryWarning,
                sizeArg.GetLocation(), byteSize, 32768));
        }

        context.Writer.AppendKeyword("__shared__");
        context.Writer.AppendSpace();
        context.Writer.Append(cppElementType);
        context.Writer.AppendSpace();
        context.Writer.Append(v.Identifier.Text);
        using (context.Writer.Brackets())
        {
            context.Visit(sizeArg);
        }

        context.Writer.Append(";");
        if (i < node.Declaration.Variables.Count - 1)
        {
            context.Writer.AppendLine();
        }
    }

    private static void WriteDynamicSharedMemory(VariableDeclaratorSyntax v, ExpressionSyntax sizeArg, string cppElementType,
        LocalDeclarationStatementSyntax node, int i, RoslynToCudaVisitor context)
    {
        if (!context.HasEmittedDynamicSmemBase)
        {
            context.Writer.Append("extern __shared__ char __gsharp_dynamic_smem");
            using (context.Writer.Brackets()) { }
            context.Writer.AppendLine(";");
            context.Writer.AppendLine("size_t __smem_offset = 0;");
            context.HasEmittedDynamicSmemBase = true;
        }

        context.Writer.Append("__smem_offset = ");
        using (context.Writer.Parentheses())
        {
            context.Writer.Append("__smem_offset + alignof");
            using (context.Writer.Parentheses()) { context.Writer.Append(cppElementType); }
            context.Writer.Append(" - 1");
        }
        context.Writer.Append(" & ~");
        using (context.Writer.Parentheses())
        {
            context.Writer.Append("alignof");
            using (context.Writer.Parentheses()) { context.Writer.Append(cppElementType); }
            context.Writer.Append(" - 1");
        }
        context.Writer.Append(";");
        context.Writer.AppendLine();

        context.Writer.Append($"{cppElementType}* {v.Identifier.Text} = ");
        using (context.Writer.Parentheses()) { context.Writer.Append($"{cppElementType}*"); }
        using (context.Writer.Parentheses()) { context.Writer.Append("__gsharp_dynamic_smem + __smem_offset"); }
        context.Writer.Append(";");
        context.Writer.AppendLine();
        context.Writer.Append("__smem_offset += ");
        using (context.Writer.Parentheses())
        {
            context.Visit(sizeArg);
        }

        context.Writer.Append($" * sizeof({cppElementType});");

        if (i < node.Declaration.Variables.Count - 1)
        {
            context.Writer.AppendLine();
        }
    }

    public static void TranspileVariableDeclaration(VariableDeclarationSyntax node, RoslynToCudaVisitor context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node.Type).Type;
        if (typeSymbol is { SpecialType: SpecialType.System_String })
        {
            context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.StringNotSupported, node.Type.GetLocation()));
        }

        if (typeSymbol is { TypeKind: TypeKind.Struct } &&
            typeSymbol.ContainingNamespace.ToDisplayString() != TypeConstants.SystemNamespace)
        {
            context.UsedStructs.Add((INamedTypeSymbol)typeSymbol);
        }

        var cppType = node.Type.IsVar ? "auto" : CudaTypeMapper.MapType(typeSymbol);
        var isPointer = typeSymbol?.TypeKind == TypeKind.Pointer;
        var baseCppType = isPointer ? cppType.TrimEnd('*').TrimEnd() : cppType;

        var splitStatements = node.Parent is LocalDeclarationStatementSyntax || node.Parent is FixedStatementSyntax;

        for (var i = 0; i < node.Variables.Count; i++)
        {
            var v = node.Variables[i];

            if (splitStatements)
            {
                context.Writer.Append(cppType);
                context.Writer.AppendSpace();
                context.Writer.Append(v.Identifier.Text);
            }
            else
            {
                if (i == 0)
                {
                    context.Writer.Append(baseCppType);
                    context.Writer.AppendSpace();
                }

                if (isPointer)
                {
                    context.Writer.Append("*");
                }
                context.Writer.Append(v.Identifier.Text);
            }

            if (v.Initializer != null)
            {
                context.Writer.AppendOperator("=");
                context.Visit(v.Initializer.Value);
            }

            if (i < node.Variables.Count - 1)
            {
                if (splitStatements)
                {
                    context.Writer.AppendSemiColon();
                    context.Writer.AppendLine();
                }
                else
                {
                    context.Writer.AppendComma();
                }
            }
        }
    }
}
