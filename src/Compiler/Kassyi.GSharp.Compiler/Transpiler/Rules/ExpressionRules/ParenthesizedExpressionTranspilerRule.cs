using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class ParenthesizedExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ParenthesizedExpressionSyntax>
{
    public static readonly ParenthesizedExpressionTranspilerRule Instance = new();

    private ParenthesizedExpressionTranspilerRule() { }

    public override void Transpile(ParenthesizedExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Writer.Parentheses())
        {
            context.Visitor.Visit(node.Expression);
        }
    }
}
