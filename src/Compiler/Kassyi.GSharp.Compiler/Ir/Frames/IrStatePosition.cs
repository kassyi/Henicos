namespace Kassyi.GSharp.Compiler.Ir.Frames;

/// <summary>The place of a top-level statement of a state machine, in the order of the linear form.</summary>
/// <param name="State">The zero-based state number.</param>
/// <param name="Index">The zero-based statement index within the state.</param>
internal readonly record struct IrStatePosition(int State, int Index) : IComparable<IrStatePosition>
{
    /// <summary>Compares by <see cref="State"/> first, then by <see cref="Index"/>.</summary>
    public int CompareTo(IrStatePosition other)
    {
        var stateComparison = State.CompareTo(other.State);
        return stateComparison != 0 ? stateComparison : Index.CompareTo(other.Index);
    }
}
