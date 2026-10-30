namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// A device function analyzed for a call whose arguments vary as <see cref="Masks"/> says: the key of the analyses in
/// progress, so that a recursive call with other argument masks is analyzed instead of being left out.
/// </summary>
internal readonly record struct CalleeCall(IMethodSymbol Callee, ArgumentMasks Masks)
{
    /// <summary>A set of calls in progress, empty.</summary>
    public static HashSet<CalleeCall> CreateSet() => [];

    public bool Equals(CalleeCall other) =>
        Masks == other.Masks && SymbolEqualityComparer.Default.Equals(Callee, other.Callee);

    public override int GetHashCode() =>
        unchecked((SymbolEqualityComparer.Default.GetHashCode(Callee) * 397) ^ Masks.GetHashCode());
}
