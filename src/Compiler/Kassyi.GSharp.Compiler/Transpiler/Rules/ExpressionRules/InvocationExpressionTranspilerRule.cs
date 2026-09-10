namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

public sealed class InvocationExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<InvocationExpressionSyntax>
{
    public static readonly InvocationExpressionTranspilerRule Instance = new();

    private static readonly ImmutableArray<IInvocationTranspilerRule> s_invocationRules =
        ImmutableArray.Create<IInvocationTranspilerRule>(
            BclIntrinsicTranspilerRule.Instance,
            GpuMacroTranspilerRule.Instance
        );

    private InvocationExpressionTranspilerRule() { }

    public override void Transpile(InvocationExpressionSyntax node, ref TranspileContext context)
    {
        if (context.SemanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol symbol)
        {
            foreach (var rule in s_invocationRules)
            {
                if (rule.TryTranspile(node, ref context, symbol))
                {
                    return;
                }
            }
        }

        context.Visitor.Visit(node.Expression);
        using (context.Writer.Parentheses())
        {
            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                context.Visitor.Visit(node.ArgumentList.Arguments[i]);
                if (i < node.ArgumentList.Arguments.Count - 1)
                {
                    context.Writer.AppendComma();
                }
            }
        }
    }
}
