using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.Generators.GSharp.Diagnostics;

namespace Kassyi.Generators.GSharp.Transpiler;

internal static class DeclarationTranspilers
{
    public static void TranspileLocalDeclarationStatement(LocalDeclarationStatementSyntax node, RoslynToCudaVisitor context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node.Declaration.Type).Type;
        if (typeSymbol?.Name == "SharedMemory" &&
            typeSymbol.ContainingNamespace.ToDisplayString() == "Kassyi.GSharp.Core")
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
            context.Writer.Append(constantValue.ToString());
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
            context.Writer.AppendLine("extern __shared__ char __gsharp_dynamic_smem[];");
            context.Writer.AppendLine("size_t __smem_offset = 0;");
            context.HasEmittedDynamicSmemBase = true;
        }

        context.Writer.Append(
            $"__smem_offset = (__smem_offset + alignof({cppElementType}) - 1) & ~(alignof({cppElementType}) - 1);");
        context.Writer.AppendLine();
        context.Writer.Append(
            $"{cppElementType}* {v.Identifier.Text} = ({cppElementType}*)(__gsharp_dynamic_smem + __smem_offset);");
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
            typeSymbol.ContainingNamespace.ToDisplayString() != "System")
        {
            context.UsedStructs.Add((INamedTypeSymbol)typeSymbol);
        }

        var cppType = CudaTypeMapper.MapType(typeSymbol);

        for (var i = 0; i < node.Variables.Count; i++)
        {
            var v = node.Variables[i];

            context.Writer.Append(cppType);
            context.Writer.AppendSpace();
            context.Writer.Append(v.Identifier.Text);

            if (v.Initializer != null)
            {
                context.Writer.AppendOperator("=");
                context.Visit(v.Initializer.Value);
            }

            if (i < node.Variables.Count - 1)
            {
                context.Writer.AppendComma();
            }
        }
    }
}
