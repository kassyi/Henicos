using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// The values that the options of <c>fuzz-diff run</c> and <c>fuzz-diff compare</c> set, before each command turns
/// them into its options record. A command's table sets only the members of its own options.
/// </summary>
internal sealed class FuzzOptionValues
{
    internal FuzzProfile Profile { get; set; } = FuzzProfile.Cpu;

    internal double Minutes { get; set; } = 5;

    internal int? Batches { get; set; }

    internal int BatchSize { get; set; } = 64;

    internal ulong FuzzSeed { get; set; } = 1UL;

    internal int? InputSeed { get; set; }

    internal List<int> AllowedIssues { get; } = [];

    internal string? SwarmFocus { get; set; }

    internal bool Metamorphic { get; set; }

    internal bool ExpectChange { get; set; }

    internal bool NoReduce { get; set; }

    internal bool NoGpuLock { get; set; }

    internal string? CompareBase { get; set; }

    internal string? WorkDir { get; set; }

    internal string? OutDir { get; set; }

    internal string? RepoRoot { get; set; }

    internal List<string> Dialects { get; } = CompareCommand.DefaultDialects();

    internal int ReduceMinutes { get; set; } = CompareCommand.DefaultReduceMinutes;
}

/// <summary>One option of a command: whether it takes a value, and the step that applies it.</summary>
/// <param name="TakesValue">Whether the option reads a value (<c>--name value</c> or <c>--name=value</c>).</param>
/// <param name="Apply">Sets the value; returns the error message, or <see langword="null"/> when it is valid.</param>
internal sealed record FuzzOptionSpec(bool TakesValue, Func<FuzzOptionValues, string, string?> Apply);

/// <summary>
/// The option table of <c>fuzz-diff run</c> and <c>fuzz-diff compare</c>: one row per option and one parse loop, so
/// that an option shared by both commands has one parser.
/// </summary>
internal static class FuzzOptionTable
{
    /// <summary>The largest <c>--batch-size</c>.</summary>
    internal const int MaxBatchSize = 256;

    /// <summary>Gets the rows that <c>run</c> and <c>compare</c> share: the generation options and the flags.</summary>
    internal static IEnumerable<KeyValuePair<string, FuzzOptionSpec>> SharedRows { get; } =
    [
        new("--batches", Value(ParseBatches)),
        new("--batch-size", Value(ParseBatchSize)),
        new("--fuzz-seed", Value(ParseFuzzSeed)),
        new("--input-seed", Value(ParseInputSeed)),
        new("--allow-issue", Value(ParseAllowIssue)),
        new("--swarm-focus", Value(ParseSwarmFocus)),
        new("--metamorphic", Flag(static v => v.Metamorphic = true)),
        new("--expect-change", Flag(static v => v.ExpectChange = true)),
        new("--no-reduce", Flag(static v => v.NoReduce = true)),
    ];

    /// <summary>Builds a table from the shared rows and the rows of one command.</summary>
    internal static Dictionary<string, FuzzOptionSpec> Create(params KeyValuePair<string, FuzzOptionSpec>[] ownRows)
    {
        var table = new Dictionary<string, FuzzOptionSpec>(StringComparer.Ordinal);
        foreach (var row in SharedRows)
        {
            table.Add(row.Key, row.Value);
        }

        foreach (var row in ownRows)
        {
            table.Add(row.Key, row.Value);
        }

        return table;
    }

    /// <summary>A row for an option without a value; a value written as <c>--name=value</c> is ignored.</summary>
    internal static FuzzOptionSpec Flag(Action<FuzzOptionValues> set) =>
        new(TakesValue: false, (values, _) =>
        {
            set(values);
            return null;
        });

    /// <summary>A row for an option with a value.</summary>
    internal static FuzzOptionSpec Value(Func<FuzzOptionValues, string, string?> apply) => new(TakesValue: true, apply);

    /// <summary>
    /// Applies every argument through <paramref name="table"/>. <c>--help</c> or <c>-h</c> stops the loop and sets
    /// <paramref name="showHelp"/>; an unknown option, a missing value or an invalid value stops it with an error.
    /// </summary>
    internal static bool TryParse(
        ReadOnlySpan<string> args,
        IReadOnlyDictionary<string, FuzzOptionSpec> table,
        FuzzOptionValues values,
        out bool showHelp,
        out string? error)
    {
        showHelp = false;
        error = null;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            (string name, string? inlineValue) = SplitInlineValue(arg);
            if (name is "--help" or "-h")
            {
                showHelp = true;
                return true;
            }

            if (!table.TryGetValue(name, out var spec))
            {
                error = $"Unknown option '{arg}'.";
                return false;
            }

            string value = inlineValue ?? string.Empty;
            if (spec.TakesValue && inlineValue is null)
            {
                if (i + 1 >= args.Length)
                {
                    error = $"{name} requires a value.";
                    return false;
                }

                value = args[++i];
            }

            error = spec.Apply(values, value);
            if (error is not null)
            {
                return false;
            }
        }

        return true;
    }

    private static (string Name, string? InlineValue) SplitInlineValue(string arg)
    {
        int equals = arg.StartsWith("--", StringComparison.Ordinal) ? arg.IndexOf('=') : -1;
        return equals >= 0 ? (arg[..equals], arg[(equals + 1)..]) : (arg, null);
    }

    private static string? ParseBatches(FuzzOptionValues values, string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int batches) || batches <= 0)
        {
            return "--batches requires a positive integer.";
        }

        values.Batches = batches;
        return null;
    }

    private static string? ParseBatchSize(FuzzOptionValues values, string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) ||
            size is < 1 or > MaxBatchSize)
        {
            return "--batch-size requires an integer in [1, 256].";
        }

        values.BatchSize = size;
        return null;
    }

    private static string? ParseFuzzSeed(FuzzOptionValues values, string value)
    {
        if (!FuzzRandom.TryParseSeed(value, out ulong seed))
        {
            return $"Invalid --fuzz-seed '{value}'.";
        }

        values.FuzzSeed = seed;
        return null;
    }

    private static string? ParseInputSeed(FuzzOptionValues values, string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
        {
            return "--input-seed requires an integer.";
        }

        values.InputSeed = seed;
        return null;
    }

    private static string? ParseAllowIssue(FuzzOptionValues values, string value)
    {
        var digits = value.AsSpan().TrimStart('#');
        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int issue) || issue <= 0)
        {
            return "--allow-issue requires a positive integer issue number.";
        }

        if (!values.AllowedIssues.Contains(issue))
        {
            values.AllowedIssues.Add(issue);
        }

        return null;
    }

    private static string? ParseSwarmFocus(FuzzOptionValues values, string value)
    {
        if (value.Length == 0 || !FuzzSwarmConfig.TryParseFocus(value, out _))
        {
            return "--swarm-focus requires 'default', 'coalesce-assign-value', or 'tuple-deconstruct-complex'.";
        }

        values.SwarmFocus = value;
        return null;
    }
}
