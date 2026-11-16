#nullable enable
using System.Text;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>A GPU method of the IR corpus: its file, relative to the shared test project, and its type and name.</summary>
internal readonly record struct IrCorpusMethod(string File, string Method);

/// <summary>
/// Counts of the IR corpus tests, one entry per GPU method and category, written as YAML with one flow mapping per line
/// and sorted by file and method. Pull requests that add kernels in different methods then change different lines, so
/// they merge without a conflict; a total per category for the whole corpus would be one line that every such pull
/// request changes.
/// </summary>
internal sealed class IrCorpusCounts
{
    private readonly ConcurrentDictionary<string, int> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _totals = new(StringComparer.Ordinal);

    /// <summary>
    /// Adds one to the entry of <paramref name="method"/> with <paramref name="fields"/>, the keys and values of the
    /// category in the order they are written.
    /// </summary>
    public void Add(IrCorpusMethod method, params (string Key, string Value)[] fields)
    {
        var entry = new StringBuilder("- {file: ").Append(Quote(method.File))
            .Append(", method: ").Append(Quote(method.Method));
        var category = new StringBuilder();
        foreach (var (key, value) in fields)
        {
            entry.Append(", ").Append(key).Append(": ").Append(Quote(value));
            category.Append(category.Length == 0 ? "" : " ").Append(value);
        }

        _entries.AddOrUpdate(entry.Append(", count: ").ToString(), 1, static (_, count) => count + 1);
        _totals.AddOrUpdate(category.ToString(), 1, static (_, count) => count + 1);
    }

    /// <summary>The total of each category over the whole corpus, one line each, for the test output.</summary>
    public IEnumerable<string> Totals() =>
        _totals.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{pair.Value,6}  {pair.Key}");

    /// <summary>The sum of every count.</summary>
    public int Sum => _totals.Values.Sum();

    /// <summary>
    /// The file: <paramref name="header"/> as <c>#</c> comment lines, then the entries sorted by file, method and
    /// category, each ended by <c>\n</c>.
    /// </summary>
    public string Render(IEnumerable<string> header)
    {
        var lines = header.Select(static line => line.Length == 0 ? "#" : "# " + line)
            .Concat(_entries
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => pair.Key + pair.Value + "}"));
        return string.Join("\n", lines) + "\n";
    }

    // A YAML double-quoted scalar: a reason such as "known, an initializer writes the new value" holds a comma, which
    // ends a plain scalar inside a flow mapping
    private static string Quote(string value)
    {
        var quoted = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            if (c is '"' or '\\')
            {
                quoted.Append('\\');
            }

            quoted.Append(c);
        }

        return quoted.Append('"').ToString();
    }
}
