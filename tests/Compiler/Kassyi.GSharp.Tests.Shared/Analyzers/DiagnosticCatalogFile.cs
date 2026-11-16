#nullable enable
using Kassyi.GSharp.Compiler.NativeApi;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>One entry of <c>diagnostics.yml</c>, with the keys the tests read.</summary>
internal sealed record DiagnosticCatalogEntry(string Name, string Id, string Title, string DefaultSeverity, bool IsObsolete);

/// <summary>
/// Reads <c>src/Compiler/Kassyi.GSharp.Compiler/Diagnostics/diagnostics.yml</c> with the YAML parser of the compiler,
/// which rejects a repeated key, as the build's generator does.
/// </summary>
internal static class DiagnosticCatalogFile
{
    /// <summary>The path of the catalog.</summary>
    public static string Path => System.IO.Path.Combine(RepositoryPaths.CompilerProject, "Diagnostics", "diagnostics.yml");

    /// <summary>The entries of the catalog file.</summary>
    public static List<DiagnosticCatalogEntry> Read()
    {
        Assert.True(File.Exists(Path), $"diagnostics.yml not found at {Path}");
        return Parse(File.ReadAllText(Path));
    }

    /// <summary>The entries of a catalog text; a text that is not a list of mappings fails the test.</summary>
    public static List<DiagnosticCatalogEntry> Parse(string text)
    {
        var root = Assert.IsType<YamlSequence>(YamlSubsetParser.Parse(text));
        return
        [
            .. root.Items.Select(static item =>
            {
                var entry = Assert.IsType<YamlMapping>(item);
                return new DiagnosticCatalogEntry(
                    Scalar(entry, "name"),
                    Scalar(entry, "id"),
                    Scalar(entry, "title"),
                    Scalar(entry, "defaultSeverity"),
                    entry.TryGetScalar("isObsolete", out var obsolete) && obsolete == "true");
            })
        ];
    }

    private static string Scalar(YamlMapping entry, string key) =>
        entry.TryGetScalar(key, out var value) ? value : "";
}
