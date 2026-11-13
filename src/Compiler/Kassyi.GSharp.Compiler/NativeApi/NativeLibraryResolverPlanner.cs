namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Plans the generated <c>DllImportResolver</c>: one entry per library name, whatever number of definitions bind it.
/// </summary>
/// <remarks>
/// The runtime resolves a <c>[DllImport]</c> library name once per assembly, so every class that binds the same name
/// gets the same handle: the candidates of all those definitions are probed together. Definitions are taken in the
/// ordinal order of their file paths, with <c>\</c> read as <c>/</c>, and each list keeps its order, so the result
/// depends neither on the order in which the build passes the files nor on the operating system; a repeated candidate
/// is probed once, and the logical name comes last unless a list already names it.
/// </remarks>
internal static class NativeLibraryResolverPlanner
{
    public static ImmutableArray<NativeLibraryCandidates> Plan(
        ImmutableArray<(string Path, NativeApiDefinition Definition)> definitions)
    {
        var resolved = ImmutableArray.CreateBuilder<(string Path, NativeApiDefinition Definition)>();
        definitions
            .AsStackEnumerable()
            .Where(static d => RequiresResolver(d.Definition))
            .AddTo(resolved);
        resolved.Sort(static (a, b) => ComparePaths(a.Path, b.Path));
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

    /// <summary>
    /// Returns <see langword="true"/> when the library of <paramref name="def"/> cannot be found by the default
    /// probing logic: it declares platform candidates or is given as a relative or absolute path.
    /// </summary>
    public static bool RequiresResolver(NativeApiDefinition def)
    {
        if (def.BindingKind != NativeApiBindingKind.Library)
        {
            return false;
        }

        if (def.LibraryNames is { IsEmpty: false })
        {
            return true;
        }

        var libraryName = def.LibraryName ?? string.Empty;
        return libraryName.IndexOf('/') >= 0 || libraryName.IndexOf('\\') >= 0;
    }

    // Ordinal comparison of two paths with every \ read as /, so that Windows and Unix order the same files alike:
    // \ (92) and / (47) sort on different sides of digits and upper-case letters
    internal static int ComparePaths(string a, string b)
    {
        var length = Math.Min(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            var difference = NormalizeSeparator(a[i]) - NormalizeSeparator(b[i]);
            if (difference != 0)
            {
                return difference;
            }
        }

        return a.Length - b.Length;
    }

    private static char NormalizeSeparator(char c) => c == '\\' ? '/' : c;

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
