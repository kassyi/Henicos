using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class BinaryExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<BinaryExpressionSyntax>
{
    public static readonly BinaryExpressionTranspilerRule Instance = new();

    private BinaryExpressionTranspilerRule() { }

    public override void Transpile(BinaryExpressionSyntax node, ref TranspileContext context)
    {
        if (node.IsKind(SyntaxKind.CoalesceExpression))
        {
            using (context.Writer.Brackets())
            {
                context.Writer.Append("&");
            }

            context.Writer.Append("() ");
            using (context.Writer.Scope())
            {
                context.Writer.Append("auto __coalesce_tmp = ");
                context.Visitor.Visit(node.Left);
                context.Writer.AppendSemiColonLine();

                context.Writer.Append("return __coalesce_tmp.HasValue ? __coalesce_tmp.Value : ");
                context.Visitor.Visit(node.Right);
                context.Writer.AppendSemiColonLine();
            }

            context.Writer.AppendParentheses();

            return;
        }

        context.Visitor.Visit(node.Left);

        var opText = node.OperatorToken.Text;
        if (opText.Length > 0 && char.IsLetter(opText[0]))
        {
            context.Writer.Append($" {opText} ");
        }
        else
        {
            context.Writer.AppendOperator(opText);
        }

        context.Visitor.Visit(node.Right);
    }
}
