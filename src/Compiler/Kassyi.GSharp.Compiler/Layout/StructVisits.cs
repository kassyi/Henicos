namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// The types a search over struct field types has already looked into, for a search whose answer does not depend on
/// the path to a struct ("does any field, at any depth, hold X"): each struct is searched once, so the search is
/// linear in the number of distinct structs, also for a struct with several fields of its own type (CS0523) or of
/// arrays of its own type.
/// </summary>
/// <remarks>
/// A walk whose answer depends on the path (a layout, which a struct already on the path cannot have) uses
/// <see cref="StructPath"/> instead.
/// </remarks>
internal readonly ref struct StructVisits
{
    // [WHY] The searches run for every kernel parameter and struct the rules check; the search gives its emptied set
    // back, so a thread allocates one set rather than one per search
    [ThreadStatic]
    private static HashSet<ITypeSymbol>? t_spare;

    private readonly HashSet<ITypeSymbol> _visited;

    private StructVisits(HashSet<ITypeSymbol> visited) => _visited = visited;

    /// <summary>Starts a search with no type visited; dispose it when the search ends.</summary>
    public static StructVisits Start()
    {
        var visited = t_spare ?? new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        t_spare = null;
        return new StructVisits(visited);
    }

    /// <summary>
    /// Marks <paramref name="type"/> as visited; false when the search has already looked into it, or when it is a
    /// larger construction of a generic struct that holds itself (<see cref="SelfHoldingGenericStructs"/>).
    /// </summary>
    public bool TryVisit(ITypeSymbol type) =>
        !SelfHoldingGenericStructs.IsLargerConstruction(type, _visited) && _visited.Add(type);

    public void Dispose()
    {
        _visited.Clear();
        t_spare = _visited;
    }
}
