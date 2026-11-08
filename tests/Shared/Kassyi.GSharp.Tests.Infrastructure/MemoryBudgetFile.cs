#nullable enable
using System.Globalization;

namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Reads <c>tests/memory-budgets.yml</c>: one line <c>&lt;test assembly name&gt;: &lt;MB&gt;[, stress &lt;MB&gt;]</c>
/// per
/// test assembly, with <c>#</c> comments and blank lines.
/// </summary>
/// <remarks>
/// The first value is the budget of a run without Stress and Benchmark tests (<c>just test</c>); the optional
/// <c>stress</c> value applies from the moment a Stress or Benchmark test starts (the CI filter
/// <c>Category!=GpuExecution</c> runs them). Only this flat subset of YAML is accepted:
/// <c>tests/Directory.Build.props</c>
/// reads the same lines with regular expressions to set <c>System.GC.HeapHardLimit</c>, so the format must stay one
/// entry per line at column 0.
/// </remarks>
public static class MemoryBudgetFile
{
    /// <summary>
    /// The path of the budget file relative to the repository root.
    /// </summary>
    public const string RelativePath = "tests/memory-budgets.yml";

    private const string StressKeyword = "stress";

    /// <summary>
    /// Gets the absolute path of the budget file of this checkout.
    /// </summary>
    public static string FullPath => RepositoryPaths.Combine("tests", "memory-budgets.yml");

    /// <summary>
    /// Parses the text of a budget file.
    /// </summary>
    /// <param name="text">The file content.</param>
    /// <returns>The budgets per test assembly, in file order.</returns>
    /// <exception cref="FormatException">A line is not a comment, blank, or <c>name: MB[, stress MB]</c> with positive
    /// numbers, or a name repeats.</exception>
    public static IReadOnlyList<MemoryBudgetEntry> Parse(string text)
    {
        var entries = new List<MemoryBudgetEntry>();
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

            if (!TryParseLine(line, out ReadOnlySpan<char> name, out long megabytes, out long stressMegabytes))
            {
                throw new FormatException(
                    $"{RelativePath} line {lineNumber}: expected '<test assembly name>: <MB>[, stress <MB>]' "
                    + $"at column 0, got '{rawLine.TrimEnd()}'."
                );
            }

            string nameText = name.ToString();
            if (!names.Add(nameText))
            {
                throw new FormatException($"{RelativePath} line {lineNumber}: '{nameText}' is listed twice.");
            }

            entries.Add(new MemoryBudgetEntry(nameText, megabytes, stressMegabytes));
        }

        return entries;
    }

    // <name>: <MB>[, stress <MB>]
    private static bool TryParseLine(
        ReadOnlySpan<char> line,
        out ReadOnlySpan<char> name,
        out long megabytes,
        out long stressMegabytes)
    {
        stressMegabytes = 0;
        megabytes = 0;
        int colon = line.IndexOf(':');
        name = colon < 0 ? [] : line[..colon];
        if (name.IsEmpty || name.Trim().Length != name.Length)
        {
            return false;
        }

        ReadOnlySpan<char> values = line[(colon + 1)..];
        int comma = values.IndexOf(',');
        ReadOnlySpan<char> first = (comma < 0 ? values : values[..comma]).Trim();
        if (!TryParseMegabytes(first, out megabytes))
        {
            return false;
        }

        if (comma < 0)
        {
            return true;
        }

        ReadOnlySpan<char> stress = values[(comma + 1)..].Trim();
        return stress.StartsWith(StressKeyword, StringComparison.Ordinal)
            && stress.Length > StressKeyword.Length
            && char.IsWhiteSpace(stress[StressKeyword.Length])
            && TryParseMegabytes(stress[StressKeyword.Length..].Trim(), out stressMegabytes);
    }

    private static bool TryParseMegabytes(ReadOnlySpan<char> text, out long megabytes) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out megabytes) && megabytes > 0;
}

/// <summary>
/// The memory budgets of one test assembly in <c>tests/memory-budgets.yml</c>.
/// </summary>
/// <param name="Name">The test assembly (project) name.</param>
/// <param name="Megabytes">The budget of a run without Stress and Benchmark tests, in MB.</param>
/// <param name="StressMegabytes">The budget once a Stress or Benchmark test has started, in MB; <c>0</c> when the
/// assembly has none (the first budget then applies throughout).</param>
public readonly record struct MemoryBudgetEntry(string Name, long Megabytes, long StressMegabytes);
