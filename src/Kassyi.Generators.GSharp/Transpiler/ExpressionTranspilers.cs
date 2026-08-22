using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.Generators.GSharp.Diagnostics;

namespace Kassyi.Generators.GSharp.Transpiler;

internal static class ExpressionTranspilers
{
    public static void TranspileAssignmentExpression(AssignmentExpressionSyntax node, RoslynToCudaVisitor context)
    {
        context.Visit(node.Left);
        context.Writer.AppendOperator(node.OperatorToken.Text);
        context.Visit(node.Right);
    }

    public static void TranspileBinaryExpression(BinaryExpressionSyntax node, RoslynToCudaVisitor context)
    {
        using (context.Writer.Parentheses())
        {
            context.Visit(node.Left);
            context.Writer.AppendOperator(node.OperatorToken.Text);
            context.Visit(node.Right);
        }
    }

    public static void TranspileElementAccessExpression(ElementAccessExpressionSyntax node, RoslynToCudaVisitor context)
    {
        context.Visit(node.Expression);
        using (context.Writer.Brackets())
        {
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                context.Visit(node.ArgumentList.Arguments[i].Expression);
                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }
    }

    public static void TranspilePrefixUnaryExpression(PrefixUnaryExpressionSyntax node, RoslynToCudaVisitor context)
    {
        // For pointers: *a
        if (node.IsKind(SyntaxKind.PointerIndirectionExpression))
        {
            context.Writer.Append("*");
            context.Visit(node.Operand);
            return;
        }

        var op = node.OperatorToken.Text;
        context.Writer.Append(op);
        context.Visit(node.Operand);
    }

    public static void TranspileCastExpression(CastExpressionSyntax node, RoslynToCudaVisitor context)
    {
        using (context.Writer.Parentheses())
        {
            context.Writer.Append(CudaTypeMapper.MapType(context.SemanticModel.GetTypeInfo(node.Type).Type));
        }

        context.Visit(node.Expression);
    }

    public static void TranspileParenthesizedExpression(ParenthesizedExpressionSyntax node, RoslynToCudaVisitor context)
    {
        using (context.Writer.Parentheses())
        {
            context.Visit(node.Expression);
        }
    }

    public static void TranspilePostfixUnaryExpression(PostfixUnaryExpressionSyntax node, RoslynToCudaVisitor context)
    {
        context.Visit(node.Operand);
        context.Writer.Append(node.OperatorToken.Text);
    }

    public static void TranspileMemberAccessExpression(MemberAccessExpressionSyntax node, RoslynToCudaVisitor context)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is { ContainingType: not null })
        {
            var typeName = symbol.ContainingType.Name;
            if (CudaIntrinsicsMapper.TryMap(typeName, node.Name.Identifier.Text, out var mappedName))
            {
                context.Writer.Append(mappedName);
                return;
            }
        }

        context.Visit(node.Expression);
        context.Writer.Append(".");
        context.Writer.Append(node.Name.Identifier.Text);
    }

    public static void TranspileInvocationExpression(InvocationExpressionSyntax node, RoslynToCudaVisitor context)
    {
        context.Visit(node.Expression);
        using (context.Writer.Parentheses())
        {
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                context.Visit(node.ArgumentList.Arguments[i]);
                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }
    }

    public static void TranspileArgument(ArgumentSyntax node, RoslynToCudaVisitor context)
    {
        if (node.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || node.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
        {
            context.Writer.Append("&");
        }

        context.Visit(node.Expression);
    }

    public static void TranspileObjectCreationExpression(ObjectCreationExpressionSyntax node, RoslynToCudaVisitor context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node).Type;
        if (typeSymbol is { Name: "SharedMemory" } &&
            typeSymbol.ContainingNamespace.ToDisplayString() == "Kassyi.GSharp.Core")
        {
            context.DefaultVisit(node);
            return;
        }

        context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ObjectCreationNotSupported, node.GetLocation()));
        context.DefaultVisit(node);
    }

    public static void TranspileImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node, RoslynToCudaVisitor context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node).Type;
        if (typeSymbol is { Name: "SharedMemory" } &&
            typeSymbol.ContainingNamespace.ToDisplayString() == "Kassyi.GSharp.Core")
        {
            context.DefaultVisit(node);
            return;
        }

        context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ObjectCreationNotSupported, node.GetLocation()));
        context.DefaultVisit(node);
    }
}
