namespace Kassyi.GSharp.Compiler.Ir.Decisions;

/// <summary>Decisions for an <see cref="Expressions.IrElementReference"/> node (#1561).</summary>
/// <param name="check">Whether the array receiver needs a null check.</param>
/// <param name="site">The error site when <paramref name="check"/> is <see cref="IrElementCheck.NullChecked"/>.</param>
/// <param name="indexNeedsOverflowCheck">
/// Whether an index that C# converts to <see cref="ulong"/> needs an overflow check (matching
/// <see cref="ArrayElementIndex.NeedsOverflowCheck"/>).
/// </param>
internal sealed class IrElementDecision(IrElementCheck check, IrErrorSite? site, bool indexNeedsOverflowCheck)
    : IrDecision
{
    /// <summary>Whether the array receiver needs a null check.</summary>
    public IrElementCheck Check { get; } = check;

    /// <summary>The error site when <see cref="Check"/> is <see cref="IrElementCheck.NullChecked"/>.</summary>
    public IrErrorSite? Site { get; } = site;

    /// <summary>
    /// Whether an index that C# converts to <see cref="ulong"/> needs a runtime overflow check (matching
    /// <see cref="ArrayElementIndex.NeedsOverflowCheck"/>).
    /// </summary>
    public bool IndexNeedsOverflowCheck { get; } = indexNeedsOverflowCheck;
}
