namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

/// <summary>
/// Defines a non-generic strategy for transpiling a Roslyn <see cref="SyntaxNode"/> into target dialect code.
/// </summary>
/// <remarks>
/// <para>
/// This interface serves as the polymorphic entry point used by AST dispatchers and visitors.
/// Concrete rules should derive from <see cref="SyntaxNodeTranspilerRuleBase{TNode}"/> to benefit from safe node downcasting.
/// </para>
/// <para><b>Thread Safety:</b> Implementations must be stateless and thread-safe for concurrent AST traversal.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment). Not executed within GPU kernels.</para>
/// </remarks>
public interface ISyntaxNodeTranspilerRule
{
    /// <summary>
    /// Transpiles the specified syntax node and writes the resulting target code into the context's source writer.
    /// </summary>
    /// <param name="node">The Roslyn syntax node to transpile.</param>
    /// <param name="context">The active transpilation context maintaining output writers, semantic models, and symbol mappings.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node"/> is <see langword="null"/>.</exception>
    void Transpile(SyntaxNode node, ref TranspileContext context);
}

/// <summary>
/// Defines a strongly-typed strategy for transpiling a specific type of Roslyn syntax node into target dialect code.
/// </summary>
/// <typeparam name="TNode">The specific <see cref="SyntaxNode"/> type handled by this rule.</typeparam>
/// <remarks>
/// <para><b>Thread Safety:</b> Implementations must be stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public interface ISyntaxNodeTranspilerRule<in TNode> : ISyntaxNodeTranspilerRule where TNode : SyntaxNode
{
    /// <summary>
    /// Transpiles the specified strongly-typed syntax node into the current transpilation context.
    /// </summary>
    /// <param name="node">The strongly-typed syntax node to transpile.</param>
    /// <param name="context">The active transpilation context.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node"/> is <see langword="null"/>.</exception>
    void Transpile(TNode node, ref TranspileContext context);
}

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

