namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class CastExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<CastExpressionSyntax>
{
    public static readonly CastExpressionTranspilerRule Instance = new();

    private CastExpressionTranspilerRule() { }

    public override void Transpile(CastExpressionSyntax node, ref TranspileContext context)
    {
        using (context.Writer.Parentheses())
        {
            context.AppendType(context.SemanticModel.GetTypeInfo(node.Type).Type);
        }

        context.Visitor.Visit(node.Expression);
    }
}
