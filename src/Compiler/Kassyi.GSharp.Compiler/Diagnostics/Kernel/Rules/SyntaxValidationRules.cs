namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// The registered rules (<see cref="DiagnosticRuleRegistry.Rules"/>) that check nodes of type
/// <typeparamref name="TNode"/>, in registration order, collected once per node type.
/// </summary>
/// <typeparam name="TNode">The syntax node type the rules check.</typeparam>
internal static class SyntaxValidationRules<TNode> where TNode : SyntaxNode
{
    public static readonly ImmutableArray<ISyntaxValidationRule<TNode>> All = Collect();

    private static ImmutableArray<ISyntaxValidationRule<TNode>> Collect()
    {
        var builder = ImmutableArray.CreateBuilder<ISyntaxValidationRule<TNode>>();
        DiagnosticRuleRegistry.Rules
            .AsStackEnumerable()
            .OfType<ISyntaxValidationRule<TNode>>()
            .AddTo(builder);
        return builder.ToImmutable();
    }
}
