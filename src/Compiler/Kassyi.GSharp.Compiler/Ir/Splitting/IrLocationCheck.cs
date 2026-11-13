namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// Where C# checks a location whose parts are evaluated before a split (a null array, an index out of range), and so
/// whether <see cref="IrLocationSpills"/> puts an <see cref="IrSpillCheck"/> after its parts.
/// </summary>
internal enum IrLocationCheck
{
    /// <summary>Where it is selected, before the split: every element of an array or a span gets a check.</summary>
    WhereSelected,

    /// <summary>
    /// The target of a simple assignment: an array element is checked when the value is stored, after the split, so
    /// it gets no check; a span element is selected by its indexer before the value and gets one.
    /// </summary>
    AtStore,

    /// <summary>
    /// The target of a compound assignment: the read of the target right after its parts checks it
    /// (<see cref="IrSpillRole.CompoundRead"/>), so no element below it gets a check.
    /// </summary>
    ByRead
}
