using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class ConditionalExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ConditionalExpressionSyntax>
{
    public static readonly ConditionalExpressionTranspilerRule Instance = new();

    private ConditionalExpressionTranspilerRule() { }

    public override void Transpile(ConditionalExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Condition);
            context.Writer.Append(" ? ");

            // Handle 'ref' conditionals if present
            if (node.WhenTrue is RefExpressionSyntax refTrue)
            {
                context.Visitor.Visit(refTrue.Expression);
            }
            else
            {
                context.Visitor.Visit(node.WhenTrue);
            }

            context.Writer.Append(" : ");

            if (node.WhenFalse is RefExpressionSyntax refFalse)
            {
                context.Visitor.Visit(refFalse.Expression);
            }
            else
            {
                context.Visitor.Visit(node.WhenFalse);
            }
        }
    }
}
