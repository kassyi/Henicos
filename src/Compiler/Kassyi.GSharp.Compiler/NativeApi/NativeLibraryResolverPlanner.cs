namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Plans the generated <c>DllImportResolver</c>: one entry per library name, whatever number of definitions bind it.
/// </summary>
/// <remarks>
/// The runtime resolves a <c>[DllImport]</c> library name once per assembly, so every class that binds the same name
/// gets the same handle: the candidates of all those definitions are probed together. Definitions are taken in the
/// ordinal order of their file paths and each list keeps its order, so the result does not depend on the order in which
/// the build passes the files; a repeated candidate is probed once, and the logical name comes last unless a list
/// already names it.
/// </remarks>
internal static class NativeLibraryResolverPlanner
{
    public static ImmutableArray<NativeLibraryCandidates> Plan(
        ImmutableArray<(string Path, NativeApiDefinition Definition)> definitions)
    {
        var resolved = ImmutableArray.CreateBuilder<(string Path, NativeApiDefinition Definition)>();
        definitions
            .AsStackEnumerable()
            .Where(static d => NativeBindingEmitter.RequiresResolver(d.Definition))
            .AddTo(resolved);
        resolved.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));
        var ordered = resolved.ToImmutable();

        var libraryNames = new SortedSet<string>(StringComparer.Ordinal);
        ordered
            .AsStackEnumerable()
            .Select(static d => d.Definition.LibraryName ?? string.Empty)
            .AddTo(libraryNames);

        var builder = ImmutableArray.CreateBuilder<NativeLibraryCandidates>(libraryNames.Count);
        foreach (var libraryName in libraryNames)
        {
            builder.Add(new NativeLibraryCandidates(
                libraryName,
                Merge(ordered, libraryName, static names => names.Windows),
                Merge(ordered, libraryName, static names => names.Linux),
                Merge(ordered, libraryName, static names => names.OSX)
            ));
        }

        return builder.MoveToImmutable();
    }

    // The candidates of one platform of every definition of libraryName, in order and each once, then libraryName
    private static EquatableArray<string> Merge(
        ImmutableArray<(string Path, NativeApiDefinition Definition)> ordered,
        string libraryName,
        Func<NativeLibraryNames, ImmutableArray<string>> platform)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var candidates = ImmutableArray.CreateBuilder<string>();
        foreach (var (_, definition) in ordered)
        {
            if (definition.LibraryName != libraryName || definition.LibraryNames is not { } names)
            {
                continue;
            }

            foreach (var candidate in platform(names))
            {
                if (seen.Add(candidate))
                {
                    candidates.Add(candidate);
                }
            }
        }

        if (seen.Add(libraryName))
        {
            candidates.Add(libraryName);
        }

        return candidates.ToImmutable().AsEquatableArray();
    }
}
