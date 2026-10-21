using System.Globalization;
using Kassyi.GSharp.Naming;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Derives the names that share the global C++ scope (types, template parameters) from their symbols with
/// <see cref="GSharpNameEncoding.Encode"/>, so that distinct C# paths never share one C++ name.
/// </summary>
internal static class GlobalCppNames
{
    /// <summary>Names longer than this are shortened by <see cref="Shorten"/>.</summary>
    public const int ShortenThreshold = 200;

    private const int HashLength = 10;
    private const string HashMarker = "_h";

    // Shortened name -> full name. Only names over the threshold are recorded, so the table stays small.
    private static readonly ConcurrentDictionary<string, string> s_shortenedNames = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="type"/> is a user struct whose definition G# emits itself. Other types
    /// (<c>System_Nullable</c>, shared memory) are defined by the prelude under their legacy spelling.
    /// </summary>
    public static bool IsEmittedUserType(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Struct && !type.ContainingNamespace.IsSystemOrNumerics() && !type.IsSharedMemoryType();

    /// <summary>Returns <c>__gsharp_ty_{path}</c> for the definition of <paramref name="type"/>.</summary>
    /// <remarks>
    /// A constructed type has the name of its definition; C++ applies the type arguments as template arguments.
    /// </remarks>
    public static string GetTypeName(INamedTypeSymbol type) =>
        Shorten(GSharpNameEncoding.TypePrefix, GSharpNameEncoding.Encode(GetTypePath(type)));

    /// <summary>Returns <c>__gsharp_tp_{name}</c>.</summary>
    public static string GetTypeParameterName(ITypeParameterSymbol parameter)
    {
        var builder = new StringBuilder(GSharpNameEncoding.TypeParameterPrefix);
        GSharpNameEncoding.AppendEscapedName(builder, parameter.Name);
        return builder.ToString();
    }

    /// <summary>The C# path of the definition of <paramref name="type"/>: namespaces, containing types and the type.</summary>
    public static GlobalNamePath GetTypePath(INamedTypeSymbol type)
    {
        var segments = ImmutableArray.CreateBuilder<GlobalNameSegment>();
        for (ISymbol? symbol = type.OriginalDefinition; symbol is not null and not INamespaceSymbol { IsGlobalNamespace: true };
             symbol = symbol.ContainingSymbol)
        {
            var named = symbol as INamedTypeSymbol;
            var fileId = named is { IsFileLocal: true }
                ? GetFileOrdinal(named).ToString("x", CultureInfo.InvariantCulture)
                : null;
            segments.Add(new GlobalNameSegment(symbol.Name, arity: named?.Arity ?? 0, fileId: fileId));
        }

        segments.Reverse();
        return new GlobalNamePath(segments.ToImmutable());
    }

    // [WHY] A file type is identified by its position among the file types with the same name and arity, ordered by
    // file path and then declaration position. Paths only order the candidates and never reach the name, so the name
    // does not depend on the machine; adding an unrelated file does not change it either.
    private static int GetFileOrdinal(INamedTypeSymbol type)
    {
        if (type.ContainingSymbol is not INamespaceOrTypeSymbol container)
        {
            return 0;
        }

        var members = container.GetTypeMembers(type.Name, type.Arity);
        var candidates = new INamedTypeSymbol[members.Length];
        var count = 0;
        foreach (var member in members)
        {
            if (member.IsFileLocal)
            {
                candidates[count++] = member;
            }
        }

        Array.Sort(candidates, 0, count, FileTypeOrderComparer.Instance);
        for (var i = 0; i < count; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(candidates[i], type))
            {
                return i;
            }
        }

        throw new InvalidOperationException(
            $"The file type '{type.Name}' in '{FileTypeOrderComparer.GetFirstLocation(type).SourceTree?.FilePath}' is not a member of its containing symbol.");
    }

    private sealed class FileTypeOrderComparer : IComparer<INamedTypeSymbol>
    {
        public static readonly FileTypeOrderComparer Instance = new();

        public int Compare(INamedTypeSymbol? x, INamedTypeSymbol? y)
        {
            var left = GetFirstLocation(x);
            var right = GetFirstLocation(y);
            var byPath = ComparePaths(left.SourceTree?.FilePath ?? string.Empty, right.SourceTree?.FilePath ?? string.Empty);
            return byPath != 0 ? byPath : left.SourceSpan.Start.CompareTo(right.SourceSpan.Start);
        }

        // Ordinal, with '\' read as '/' so that Windows and WSL builds order the same files the same way.
        private static int ComparePaths(string x, string y)
        {
            var length = Math.Min(x.Length, y.Length);
            for (var i = 0; i < length; i++)
            {
                var difference = Normalize(x[i]).CompareTo(Normalize(y[i]));
                if (difference != 0)
                {
                    return difference;
                }
            }

            return x.Length.CompareTo(y.Length);
        }

        private static char Normalize(char c) => c == '\\' ? '/' : c;

        public static Location GetFirstLocation(INamedTypeSymbol? type) =>
            type is { Locations.Length: > 0 } ? type.Locations[0] : Location.None;
    }

    /// <summary>
    /// Returns <paramref name="prefix"/> + <paramref name="body"/>, or, when that exceeds <see cref="ShortenThreshold"/>,
    /// its head followed by <c>_h</c> and a 10-digit FNV-1a 64 hash of the whole name.
    /// </summary>
    /// <exception cref="InvalidOperationException">Two different names shorten to the same name.</exception>
    public static string Shorten(string prefix, string body)
    {
        var full = new StringBuilder(prefix.Length + body.Length).Append(prefix).Append(body).ToString();
        if (full.Length <= ShortenThreshold)
        {
            return full;
        }

        var headLength = ShortenThreshold - HashMarker.Length - HashLength;
        while (headLength > prefix.Length && full[headLength - 1] == '_')
        {
            headLength--;
        }

        var shortened = new StringBuilder(ShortenThreshold)
            .Append(full, 0, headLength)
            .Append(HashMarker)
            .Append(HashHelper.ComputeFnv1A64ShortHash(full))
            .ToString();

        var recorded = s_shortenedNames.GetOrAdd(shortened, full);
        if (!string.Equals(recorded, full, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The C++ names '{recorded}' and '{full}' both shorten to '{shortened}'.");
        }

        return shortened;
    }
}
