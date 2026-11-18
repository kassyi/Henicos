namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>An unconditional jump in the linear form: <c>goto &lt;target&gt;;</c>.</summary>
/// <param name="target">The label symbol jumped to.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrGoto(IrLabelSymbol target, IrSource source) : IrStatement(source)
{
    /// <summary>The label symbol jumped to.</summary>
    public IrLabelSymbol Target { get; } = target;
}
