namespace Kassyi.GSharp.Compiler.Ir.Statements;

/// <summary>
/// The declaration of one local, with its initializer; a declaration of several locals becomes one statement each.
/// </summary>
/// <param name="variable">The local.</param>
/// <param name="initializer">
/// The initializer, or <see langword="null"/>; for a <see langword="ref"/> local the location it refers to.
/// </param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrLocalDeclaration(IrVariable variable, IrExpression? initializer, IrSource source)
    : IrStatement(source)
{
    /// <summary>The local.</summary>
    public IrVariable Variable { get; } = variable;

    /// <summary>The initializer, or <see langword="null"/>.</summary>
    public IrExpression? Initializer { get; } = initializer;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrLocalDeclaration Update(IrExpression? initializer) =>
        initializer == Initializer ? this : new IrLocalDeclaration(Variable, initializer, Source);
}
