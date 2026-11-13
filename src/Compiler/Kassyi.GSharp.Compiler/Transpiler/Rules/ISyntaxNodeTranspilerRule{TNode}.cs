namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

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
