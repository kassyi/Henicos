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

    /// <summary>
    /// Whether the declaration takes its type from its initializer (C++ <c>auto</c>): written as
    /// <see langword="var"/> and with an initializer. A declaration without one, a declaring reference
    /// (<c>out var x</c>) and a variable declared again from the frame name the type.
    /// </summary>
    public bool DeducesType => Initializer is not null && Variable.DeclaredTypeForm == IrDeclaredTypeForm.Deduced;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrLocalDeclaration Update(IrExpression? initializer) =>
        initializer == Initializer ? this : KeepSpelling(new IrLocalDeclaration(Variable, initializer, Source));
}
