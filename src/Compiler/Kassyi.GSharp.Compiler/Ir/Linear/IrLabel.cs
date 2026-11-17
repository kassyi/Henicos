namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>A label definition in the linear form: <c>&lt;label&gt;: ;</c>.</summary>
/// <param name="label">The label symbol defined here.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrLabel(IrLabelSymbol label, IrSource source) : IrStatement(source)
{
    /// <summary>The label symbol defined here.</summary>
    public IrLabelSymbol Label { get; } = label;
}
