using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class PrefixUnaryExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<PrefixUnaryExpressionSyntax>
{
    public static readonly PrefixUnaryExpressionTranspilerRule Instance = new();

    private PrefixUnaryExpressionTranspilerRule() { }

    public override void Transpile(PrefixUnaryExpressionSyntax node, ref TranspileContext context)
    {
        // For pointers: *a
        if (node.IsKind(SyntaxKind.PointerIndirectionExpression))
        {
            context.Writer.AppendOperator("*");
            context.Visitor.Visit(node.Operand);
            return;
        }

        var op = node.OperatorToken.Text;
        context.Writer.Append(op);
        context.Visitor.Visit(node.Operand);
    }
}
