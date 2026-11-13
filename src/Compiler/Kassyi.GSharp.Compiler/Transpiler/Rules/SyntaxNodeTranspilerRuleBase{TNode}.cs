namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

/// <summary>
/// Provides an abstract base class for strongly-typed syntax node transpiler rules,
/// automatically handling downcasting and dispatching from the non-generic <see cref="ISyntaxNodeTranspilerRule"/>.
/// </summary>
/// <typeparam name="TNode">The specific <see cref="SyntaxNode"/> type handled by this rule.</typeparam>
/// <remarks>
/// <para><b>Thread Safety:</b> Derived classes must remain stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public abstract class SyntaxNodeTranspilerRuleBase<TNode> : ISyntaxNodeTranspilerRule<TNode> where TNode : SyntaxNode
{
    /// <inheritdoc />
    void ISyntaxNodeTranspilerRule.Transpile(SyntaxNode node, ref TranspileContext context)
    {
        if (node is TNode typedNode)
        {
            Transpile(typedNode, ref context);
        }
    }

    /// <summary>
    /// When overridden in a derived class, transpiles the strongly-typed syntax node into target dialect code.
    /// </summary>
    /// <param name="node">The strongly-typed syntax node to transpile.</param>
    /// <param name="context">The active transpilation context.</param>
    public abstract void Transpile(TNode node, ref TranspileContext context);
}
