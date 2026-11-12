namespace Kassyi.GSharp.Compiler.Helpers;

/// <summary>
/// One value per symbol for each compilation: the answers of an analysis that are computed once per symbol and live as
/// long as the compilation they were computed in. Symbols are compared with
/// <see cref="SymbolEqualityComparer.Default"/>.
/// </summary>
internal sealed class PerCompilationSymbolCache<TSymbol, TValue>
    where TSymbol : class, ISymbol
{
    private readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<TSymbol, TValue>> _values = new();

    /// <summary>The values of <paramref name="compilation"/>, shared by every caller of this cache.</summary>
    public ConcurrentDictionary<TSymbol, TValue> For(Compilation compilation) =>
        _values.GetValue(
            compilation,
            static _ => new ConcurrentDictionary<TSymbol, TValue>(SymbolEqualityComparer.Default)
        );
}
