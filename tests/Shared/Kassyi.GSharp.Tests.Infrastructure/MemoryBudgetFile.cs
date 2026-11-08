#nullable enable
using System.Globalization;

namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Reads <c>tests/memory-budgets.yml</c>: one line <c>&lt;test assembly name&gt;: &lt;budget in MB&gt;</c> per test
/// assembly, with <c>#</c> comments and blank lines.
/// </summary>
/// <remarks>
/// Only this flat subset of YAML is accepted. <c>tests/Directory.Build.props</c> reads the same lines with a regular
/// expression to set <c>System.GC.HeapHardLimit</c>, so the format must stay one entry per line at column 0.
/// </remarks>
public static class MemoryBudgetFile
{
    /// <summary>
    /// The path of the budget file relative to the repository root.
    /// </summary>
    public const string RelativePath = "tests/memory-budgets.yml";

    /// <summary>
    /// Gets the absolute path of the budget file of this checkout.
    /// </summary>
    public static string FullPath => RepositoryPaths.Combine("tests", "memory-budgets.yml");

    /// <summary>
    /// Parses the text of a budget file.
    /// </summary>
    /// <param name="text">The file content.</param>
    /// <returns>The budget in MB per test assembly name, in file order.</returns>
    /// <exception cref="FormatException">A line is not a comment, blank, or <c>name: positive integer</c>, or a name repeats.</exception>
    public static IReadOnlyList<KeyValuePair<string, long>> Parse(string text)
    {
        var entries = new List<KeyValuePair<string, long>>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        int lineNumber = 0;
        foreach (ReadOnlySpan<char> rawLine in text.AsSpan().EnumerateLines())
        {
            lineNumber++;
            int hash = rawLine.IndexOf('#');
            ReadOnlySpan<char> line = (hash < 0 ? rawLine : rawLine[..hash]).TrimEnd();
            if (line.IsEmpty)
            {
                continue;
            }

            int colon = line.IndexOf(':');
            ReadOnlySpan<char> name = colon < 0 ? [] : line[..colon];
            ReadOnlySpan<char> value = colon < 0 ? [] : line[(colon + 1)..].Trim();
            if (name.IsEmpty || name.Trim().Length != name.Length
                || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long megabytes) || megabytes <= 0)
            {
                throw new FormatException($"{RelativePath} line {lineNumber}: expected '<test assembly name>: <budget in MB>' at column 0, got '{rawLine.TrimEnd()}'.");
            }

            string nameText = name.ToString();
            if (!names.Add(nameText))
            {
                throw new FormatException($"{RelativePath} line {lineNumber}: '{nameText}' is listed twice.");
            }

            entries.Add(new KeyValuePair<string, long>(nameText, megabytes));
        }

        return entries;
    }
}
