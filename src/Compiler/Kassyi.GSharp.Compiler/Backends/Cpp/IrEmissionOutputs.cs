using Kassyi.GSharp.Compiler.Ir;

namespace Kassyi.GSharp.Compiler.Backends.Cpp;

/// <summary>
/// The tables of the method that the C++ writer adds to while it spells a body: the error sites, the includes, the
/// structs to dispose and the comparisons to sort with.
/// </summary>
internal sealed class IrEmissionOutputs(Compilation compilation, string memberName, ErrorSiteTable? errorSites,
    HashSet<string> requiredIncludes, ImmutableArray<INamedTypeSymbol>.Builder disposedStructs,
    ImmutableArray<IMethodSymbol>.Builder sortComparisons)
{
    /// <summary>Whether the method has an error site table; a device function has none.</summary>
    public bool HasErrorSiteTable => errorSites is not null;

    /// <summary>
    /// Registers the error site in the table of the method and returns its id; the same site gives the same id.
    /// </summary>
    public uint RegisterErrorSite(IrSource source, string trapKind, string message) =>
        errorSites is { } table
            ? table.Register(source.ToLocation(compilation), memberName, message, trapKind)
            : throw new InvalidOperationException("A method without an error site table registers no site.");

    /// <summary>Makes the output include <paramref name="include"/>.</summary>
    public void RequireInclude(string include) => requiredIncludes.Add(include);

    /// <summary>Makes the output write the <c>Dispose</c> of <paramref name="type"/>.</summary>
    public void RecordDisposedStruct(INamedTypeSymbol type)
    {
        if (!disposedStructs.AsStackEnumerable().Any(type, static (recorded, added) => recorded.IsEqualTo(added)))
        {
            disposedStructs.Add(type);
        }
    }

    /// <summary>Records <paramref name="comparison"/> as a comparison a sort of the method calls.</summary>
    public void RecordSortComparison(IMethodSymbol comparison)
    {
        if (!sortComparisons.AsStackEnumerable().Any(comparison, static (recorded, added) => recorded.IsEqualTo(added)))
        {
            sortComparisons.Add(comparison);
        }
    }
}
