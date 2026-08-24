using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Kassyi.GSharp.Compiler.Diagnostics;

namespace Kassyi.GSharp.Compiler.Transpiler;

internal static class ExpressionTranspilers
{
    public static void TranspileAssignmentExpression(AssignmentExpressionSyntax node, RoslynToCudaVisitor context)
    {
        if (node.Left is TupleExpressionSyntax leftTuple && node.Right is TupleExpressionSyntax rightTuple)
        {
            context.Writer.Append("[&]()");
            using (context.Writer.Scope())
            {
                for (int i = 0; i < rightTuple.Arguments.Count; i++)
                {
                    context.Writer.Append($"auto _t{i} = ");
                    context.Visit(rightTuple.Arguments[i].Expression);
                    context.Writer.AppendSemiColon();
                    context.Writer.AppendLine();
                }
                for (int i = 0; i < leftTuple.Arguments.Count; i++)
                {
                    context.Visit(leftTuple.Arguments[i].Expression);
                    context.Writer.Append($" = _t{i}");
                    context.Writer.AppendSemiColon();
                    context.Writer.AppendLine();
                }
            }
            context.Writer.Append("()");
            return;
        }

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

        if (symbol is IFieldSymbol { IsConst: true, HasConstantValue: true } field)
        {
            context.Writer.Append(field.ConstantValue!.ToString());
            switch (field.Type.SpecialType)
            {
                case SpecialType.System_UInt64:

                    context.Writer.Append("ULL");
                    break;
                case SpecialType.System_UInt32:

                    context.Writer.Append("U");
                    break;
                case SpecialType.System_Int64:

                    context.Writer.Append("LL");
                    break;
            }

            return;
        }

        var exprSymbol = context.SemanticModel.GetSymbolInfo(node.Expression).Symbol;
        if (exprSymbol is ITypeSymbol typeSym)
        {
            if (typeSym.TypeKind == TypeKind.Enum)
            {
                context.Visit(node.Expression);
                context.Writer.Append("::");
                context.Writer.Append(node.Name.Identifier.Text);
                return;
            }

            // Skip class name for global static methods
            context.Writer.Append(node.Name.Identifier.Text);
            return;
        }

        context.Visit(node.Expression);

        var isPointer = false;
        if (exprSymbol is IParameterSymbol pSym && (pSym.RefKind == RefKind.Ref || pSym.RefKind == RefKind.Out))
        {
            isPointer = true;
        }

        context.Writer.Append(isPointer ? "->" : ".");
        context.Writer.Append(node.Name.Identifier.Text);
    }


    public static void TranspileInvocationExpression(InvocationExpressionSyntax node, RoslynToCudaVisitor context)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;

        if (symbol is { ContainingType.Name: "Gpu", Name: "InlinePtx" })
        {
            TranspileInlinePtx(node, context);
            return;
        }

        if (symbol is { ContainingType.Name: "BitOperations" } &&
            symbol.ContainingType.ToString() == "System.Numerics.BitOperations")
        {
            TranspileBitOperationsInvocation(node, context, symbol);
            return;
        }

        if (symbol is { ContainingType.Name: "Gpu", Name: "JitConstant" })
        {
            if (node.ArgumentList.Arguments.Count == 1)
            {
                var expr = node.ArgumentList.Arguments[0].Expression;
                if (expr is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    context.Writer.Append(literal.Token.ValueText);
                }
                else if (expr is IdentifierNameSyntax identifier)
                {
                    context.Writer.Append(identifier.Identifier.ValueText);
                }
            }
            return;
        }

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

    private static void TranspileBitOperationsInvocation(InvocationExpressionSyntax node, RoslynToCudaVisitor context, IMethodSymbol symbol)
    {
        var is64Bit = symbol.Parameters.Length > 0 &&
                      (symbol.Parameters[0].Type.SpecialType == SpecialType.System_UInt64 ||
                       symbol.Parameters[0].Type.SpecialType == SpecialType.System_Int64);

        string functionName = symbol.Name switch
        {
            "LeadingZeroCount" => is64Bit ? "__gsharp_lzc_64" : "__gsharp_lzc_32",
            "PopCount" => is64Bit ? "__popcll" : "__popc",
            "TrailingZeroCount" => is64Bit ? "__gsharp_tzc_64" : "__gsharp_tzc_32",
            "Log2" => is64Bit ? "__gsharp_log2_64" : "__gsharp_log2_32",
            "RotateLeft" => is64Bit ? "__gsharp_rotl64" : "__gsharp_rotl32",
            "RotateRight" => is64Bit ? "__gsharp_rotr64" : "__gsharp_rotr32",
            _ => throw new InvalidOperationException($"Unsupported BitOperations method: {symbol.Name}")
        };

        context.Writer.Append(functionName);
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

    private static void TranspileInlinePtx(InvocationExpressionSyntax node, RoslynToCudaVisitor context)
    {
        if (node.ArgumentList.Arguments.Count != 1)
        {
            return;
        }

        switch (node.ArgumentList.Arguments[0].Expression)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                context.Writer.Append(literal.Token.ValueText);
                context.Writer.AppendLine();
                break;

            case InterpolatedStringExpressionSyntax interpolated:
                TranspileInterpolatedInlinePtx(interpolated, context);
                break;

            default:
                context.ReportDiagnostic?.Invoke(
                    Diagnostic.Create(GSharpDiagnostics.InlinePtxArgumentMustBeStringLiteral, node.GetLocation()));
                break;
        }
    }


    private static void TranspileInterpolatedInlinePtx(InterpolatedStringExpressionSyntax interpolated,
        RoslynToCudaVisitor context)
    {
        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    context.Writer.Append(text.TextToken.ValueText);
                    break;
                case InterpolationSyntax interpolation:
                    context.Visit(interpolation.Expression);
                    break;
            }
        }

        context.Writer.AppendLine();
    }

    public static void TranspileArgument(ArgumentSyntax node, RoslynToCudaVisitor context)
    {
        if (node.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || node.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
        {
            var symbol = context.SemanticModel.GetSymbolInfo(node.Expression).Symbol;
            var isAlreadyPointer = symbol is IParameterSymbol { RefKind: RefKind.Ref or RefKind.Out } ||
                                   symbol is ILocalSymbol { IsRef: true };

            if (!isAlreadyPointer)
            {
                context.Writer.Append("&");
            }
        }

        context.Visit(node.Expression);
    }

    public static void TranspileObjectCreationExpression(ObjectCreationExpressionSyntax node,
        RoslynToCudaVisitor context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node).Type;
        if (typeSymbol is { Name: TypeConstants.SharedMemoryType } &&
            typeSymbol.ContainingNamespace.ToDisplayString() == TypeConstants.CoreNamespace)
        {
            context.DefaultVisit(node);
            return;
        }

        if (typeSymbol is { TypeKind: TypeKind.Struct })
        {
            TranspileStructCreationCore(typeSymbol, node.Initializer, node.ArgumentList, context);
            return;
        }

        context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ObjectCreationNotSupported,
            node.GetLocation()));
        context.DefaultVisit(node);
    }

    public static void TranspileImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node,
        RoslynToCudaVisitor context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node).Type;
        switch (typeSymbol)
        {
            case { Name: TypeConstants.SharedMemoryType } when
                typeSymbol.ContainingNamespace.ToDisplayString() == TypeConstants.CoreNamespace:
                context.DefaultVisit(node);

                return;
            case { TypeKind: TypeKind.Struct }:

                TranspileStructCreationCore(typeSymbol, node.Initializer, node.ArgumentList, context);
                return;
            default:
                context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ObjectCreationNotSupported,
                    node.GetLocation()));
                context.DefaultVisit(node);
                break;
        }
    }

    private static void TranspileStructCreationCore(ITypeSymbol typeSymbol, InitializerExpressionSyntax? initializer,
        ArgumentListSyntax? argumentList, RoslynToCudaVisitor context)
    {
        var cppType = CudaTypeMapper.MapType(typeSymbol);
        if (initializer != null)
        {
            using (context.Writer.Parentheses())
            {
                using (context.Writer.Brackets())
                {
                    context.Writer.Append("&");
                }

                using (context.Writer.Parentheses())
                {
                }

                context.Writer.AppendSpace();
                using (context.Writer.Scope())
                {
                    context.Writer.Append($"{cppType} _tmp; ");
                    foreach (var expr in initializer.Expressions)
                    {
                        if (expr is AssignmentExpressionSyntax assignment)
                        {
                            context.Writer.Append("_tmp.");
                            context.Visit(assignment.Left);
                            context.Writer.Append(" = ");
                            context.Visit(assignment.Right);
                            context.Writer.Append("; ");
                        }
                    }

                    context.Writer.Append("return _tmp; ");
                }

                using (context.Writer.Parentheses())
                {
                }
            }

            return;
        }

        if (argumentList != null && argumentList.Arguments.Count > 0)
        {
            context.Writer.Append(cppType);
            using (context.Writer.Scope())
            {
                var first = true;
                foreach (var arg in argumentList.Arguments)
                {
                    if (!first)
                    {
                        context.Writer.Append(", ");
                    }

                    context.Visit(arg.Expression);
                    first = false;
                }
            }

            return;
        }

        context.Writer.Append(cppType);
    }
}


