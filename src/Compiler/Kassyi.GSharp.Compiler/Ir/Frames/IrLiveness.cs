namespace Kassyi.GSharp.Compiler.Ir.Frames;

/// <summary>What the liveness analysis decided for each tracked variable of a state machine.</summary>
/// <param name="variables">The tracked variables in non-decreasing declaration position order.</param>
/// <param name="liveAcrossSourceSplit">Bitset of variables live across a non-reconvergence split.</param>
/// <param name="liveAcrossAnySplit">Bitset of variables live across any split.</param>
/// <param name="touchedInAnotherState">
/// Bitset of variables referenced in a state other than their declaration state.
/// </param>
/// <param name="pinned">Bitset of pinned variables whose pin reaches a split.</param>
/// <param name="hasSplit">
/// Whether the state machine contains any <see cref="Linear.IrSuspend"/> or <see cref="Linear.IrResumeCall"/>.
/// </param>
internal sealed class IrLiveness(ImmutableArray<IrLivenessVariable> variables, ulong[] liveAcrossSourceSplit,
    ulong[] liveAcrossAnySplit, ulong[] touchedInAnotherState, ulong[] pinned, bool hasSplit)
{
    private readonly Dictionary<IrVariable, IrLivenessVariable> _byVariable = BuildMap(variables);

    /// <summary>The tracked variables in non-decreasing declaration position order.</summary>
    public ImmutableArray<IrLivenessVariable> Variables { get; } = variables;

    /// <summary>Whether the state machine contains any split point.</summary>
    public bool HasSplit { get; } = hasSplit;

    /// <summary>The liveness entry for <paramref name="variable"/>.</summary>
    /// <exception cref="InvalidOperationException">The variable is not tracked by this liveness.</exception>
    public IrLivenessVariable this[IrVariable variable] =>
        _byVariable.TryGetValue(variable, out var entry)
            ? entry
            : throw new InvalidOperationException($"Variable '{variable.Name}' is not tracked by this liveness.");

    /// <summary>Whether <paramref name="variable"/> is live across a source split (excluding reconvergence).</summary>
    public bool IsLiveAcrossSourceSplit(IrLivenessVariable variable) => IsSet(liveAcrossSourceSplit, variable.Index);

    /// <summary>Whether <paramref name="variable"/> is live across any split.</summary>
    public bool IsLiveAcrossAnySplit(IrLivenessVariable variable) => IsSet(liveAcrossAnySplit, variable.Index);

    /// <summary>
    /// Whether <paramref name="variable"/> is referenced in a state other than its declaration state.
    /// </summary>
    public bool IsTouchedInAnotherState(IrLivenessVariable variable) => IsSet(touchedInAnotherState, variable.Index);

    /// <summary>Whether <paramref name="variable"/> has a pin reaching a split point.</summary>
    public bool IsPinned(IrLivenessVariable variable) => IsSet(pinned, variable.Index);

    private static bool IsSet(ulong[] bits, int index) =>
        (uint)index / 64u < (uint)bits.Length && (bits[index / 64] & (1UL << (index % 64))) != 0;

    private static Dictionary<IrVariable, IrLivenessVariable> BuildMap(ImmutableArray<IrLivenessVariable> variables)
    {
        var map = new Dictionary<IrVariable, IrLivenessVariable>(variables.Length);
        foreach (var variable in variables)
        {
            map.Add(variable.Variable, variable);
        }

        return map;
    }
}
