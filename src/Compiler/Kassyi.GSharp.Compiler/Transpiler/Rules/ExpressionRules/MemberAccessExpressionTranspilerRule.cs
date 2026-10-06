using Kassyi.GSharp.Compiler.Transpiler.Helpers;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class MemberAccessExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<MemberAccessExpressionSyntax>
{
    public override void Transpile(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        var symbol = context.Query.SemanticModel.GetSymbol(node);

        if (symbol == null && node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node)
        {
            symbol = context.Query.SemanticModel.GetSymbol(invocation);
        }

        if (symbol != null && TryTranspileIntrinsic(symbol, ref context))
        {
            return;
        }

        if (symbol is IFieldSymbol { IsConst: true, HasConstantValue: true } field)
        {
            context.Emitter.Writer.AppendConstantField(field);
            return;
        }

        var memberExprSymbol = context.Query.SemanticModel.GetSymbol(node.Expression);
        if (TryTranspileEnumAccess(memberExprSymbol, node, ref context))
        {
            return;
        }

        if (memberExprSymbol is ITypeSymbol)
        {
            // Skip class name for global static methods
            context.Emitter.Writer.Append(node.Name.Identifier.Text);
            return;
        }

        if (TryTranspileStackArrayLength(node, ref context))
        {
            return;
        }

        TranspileMemberAccess(node, ref context);
    }

    private static bool TryTranspileIntrinsic(ISymbol symbol, ref TranspileContext context)
    {
        if (context.Emitter.Dialect.TryMapIntrinsic(symbol, out var mappedName))
        {
            context.Emitter.Writer.Append(mappedName);
            return true;
        }

        return false;
    }

    private static bool TryTranspileEnumAccess(ISymbol? memberExprSymbol, MemberAccessExpressionSyntax node,
        ref TranspileContext context)
    {
        if (memberExprSymbol is ITypeSymbol { TypeKind: TypeKind.Enum })
        {
            context.Visitor.Visit(node.Expression);
            context.Emitter.Writer.Append($"::{node.Name.Identifier.Text}");
            return true;
        }

        return false;
    }

    private static void TranspileMemberAccess(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        context.Visitor.Visit(node.Expression);
        context.Emitter.Writer.Append(node.IsKind(SyntaxKind.PointerMemberAccessExpression) ? "->" : ".");

        var memberName = node.Name.Identifier.Text;
        var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(node.Expression);
        if (typeSymbol != null && context.Emitter.Dialect.TryMapMemberAccess(typeSymbol, memberName, out var mappedName, out var isMethodCall))
        {
            if (isMethodCall)
            {
                context.Emitter.Writer.Append(mappedName!).AppendParentheses();
            }
            else
            {
                context.Emitter.Writer.Append(mappedName!);
            }
        }
        else
        {
            context.Emitter.Writer.Append(memberName);
        }
    }

    private static bool TryTranspileStackArrayLength(MemberAccessExpressionSyntax node, ref TranspileContext context)
    {
        if (node.Name.Identifier.Text != "Length")
        {
            return false;
        }

        var symbol = context.Query.SemanticModel.GetSymbol(node.Expression);
        if (symbol is ILocalSymbol localSym)
        {
            var syntaxRef = localSym.DeclaringSyntaxReferences.Length > 0 ? localSym.DeclaringSyntaxReferences[0] : null;
            if (syntaxRef?.GetSyntax() is not VariableDeclaratorSyntax decl)
            {
                return false;
            }

            if (decl.Initializer == null)
            {
                return false;
            }

            if (!InitializationSyntaxExtractor.TryGetArrayInitialization(decl.Initializer.Value, out var count, out var sizeExpr,
                    out _))
            {
                return false;
            }

            if (count.HasValue)
            {
                context.Emitter.Writer.Append(count.Value);
                return true;
            }

            if (sizeExpr != null)
            {
                context.Visitor.Visit(sizeExpr);
                return true;
            }
        }

        return false;
    }
}
