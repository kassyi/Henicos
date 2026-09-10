namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class ElementAccessExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ElementAccessExpressionSyntax>
{
    public static readonly ElementAccessExpressionTranspilerRule Instance = new();

    private ElementAccessExpressionTranspilerRule() { }

    public override void Transpile(ElementAccessExpressionSyntax node, ref TranspileContext context)
    {
        context.Visitor.Visit(node.Expression);
        using (context.Writer.Brackets())
        {
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                context.Visitor.Visit(node.ArgumentList.Arguments[i].Expression);
                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }
    }
}
