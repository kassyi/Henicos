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
