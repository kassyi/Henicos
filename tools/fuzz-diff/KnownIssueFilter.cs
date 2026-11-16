using Kassyi.GSharp.Compiler.NativeApi;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Describes a single tracked known issue entry loaded from <c>known-issues.yml</c>.
/// </summary>
public sealed record KnownIssueEntry(
    int Issue,
    string Kind,
    string Summary,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<string> Paths,
    string AvoidedShape,
    IReadOnlyList<string> Shapes,
    string? RemovalCondition = null,
    IReadOnlyList<string>? ClassifyOnlyPaths = null);

/// <summary>
/// Evaluates per-issue avoidance and classification rules for <see cref="ScalarFuzzGenerator"/> based on <c>known-issues.yml</c>,
/// the active execution profile (<c>M</c>, <c>C</c>, or <c>G</c>), and any user-supplied <c>--allow-issue &lt;n&gt;</c> overrides.
/// </summary>
public sealed class KnownIssueFilter
{
    /// <summary>Issue #1079: 8/16-bit integer and <see langword="char"/> <c>++</c>/<c>--</c> expression value returns widened <see langword="int"/> or post-increment value.</summary>
    public const int Issue1079NarrowIncDecValue = 1079;

    /// <summary>Issue #998: <c>??</c> between <see cref="Nullable{T}"/> of different widths or narrow <see cref="Nullable{T}"/> implicit promotion.</summary>
    public const int Issue998MixedWidthNullCoalesce = 998;

    /// <summary>Issue #984: <see cref="Nullable{T}"/> local variable declarations fail C++ / CUDA transpilation or runtime lowering.</summary>
    public const int Issue984NullableLocalVariable = 984;

    /// <summary>Issue #645: Instance methods of plain structs called from device code are not emitted or diagnosed.</summary>
    public const int Issue645PlainStructInstanceMethod = 645;

    /// <summary>Issue #1102: Nested tuple deconstruction and conditional on the right of a deconstruction fail with GSH0003.</summary>
    public const int Issue1102ComplexTupleDeconstruction = 1102;

    /// <summary>Issue #1103: <c>default(T)</c> leaves the fields of a struct with an implicit conversion uninitialized on native and CUDA.</summary>
    public const int Issue1103StructConversionDefault = 1103;

    /// <summary>Gets the complete list of issue numbers that have an avoidance handler in the generator.</summary>
    public static IReadOnlyList<int> SupportedAvoidanceIssues { get; } =
    [
        FuzzSplitKernelEmitter.StackArrayAcrossSplitIssue,
        Issue1079NarrowIncDecValue,
        Issue998MixedWidthNullCoalesce,
        Issue984NullableLocalVariable,
        Issue645PlainStructInstanceMethod,
        Issue1102ComplexTupleDeconstruction,
        Issue1103StructConversionDefault,
    ];

    private readonly HashSet<int> _avoidedIssues;

    /// <summary>
    /// Initializes a new instance of the <see cref="KnownIssueFilter"/> class.
    /// </summary>
    public KnownIssueFilter(
        string profile = "C",
        IReadOnlySet<int>? allowedIssues = null,
        IReadOnlyList<KnownIssueEntry>? entries = null)
    {
        Profile = NormalizeProfile(profile);
        AllowedIssues = allowedIssues ?? new HashSet<int>();
        Entries = entries ?? LoadEntries();

        _avoidedIssues = [];
        foreach (var entry in Entries)
        {
            if (AllowedIssues.Contains(entry.Issue))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.AvoidedShape) && AppliesToProfile(entry, Profile))
            {
                _avoidedIssues.Add(entry.Issue);
            }
        }
    }

    /// <summary>Gets the normalized profile identifier (<c>"M"</c>, <c>"C"</c>, or <c>"G"</c>).</summary>
    public string Profile { get; }

    /// <summary>Gets the set of issue numbers explicitly allowed (re-enabled) via <c>--allow-issue &lt;n&gt;</c>.</summary>
    public IReadOnlySet<int> AllowedIssues { get; }

    /// <summary>Gets the catalog of known issue entries loaded from <c>known-issues.yml</c>.</summary>
    public IReadOnlyList<KnownIssueEntry> Entries { get; }

    /// <summary>Gets the set of issue numbers currently avoided on this profile.</summary>
    public IReadOnlySet<int> ActiveAvoidedIssues => _avoidedIssues;

    /// <summary>Gets whether #1079 (8/16-bit <c>++</c>/<c>--</c> expression value) should be avoided.</summary>
    public bool AvoidNarrowIncDecValue => _avoidedIssues.Contains(Issue1079NarrowIncDecValue);

    /// <summary>Gets whether #998 (mixed-width or narrow-promoted <c>??</c> on <see cref="Nullable{T}"/>) should be avoided.</summary>
    public bool AvoidMixedWidthNullCoalesce => _avoidedIssues.Contains(Issue998MixedWidthNullCoalesce);

    /// <summary>Gets whether #984 (<see cref="Nullable{T}"/> local variable declarations) should be avoided.</summary>
    public bool AvoidNullableLocalVariables => _avoidedIssues.Contains(Issue984NullableLocalVariable);

    /// <summary>Gets whether #645 (calling plain struct instance methods from device code) should be avoided.</summary>
    public bool AvoidPlainStructInstanceMethods => _avoidedIssues.Contains(Issue645PlainStructInstanceMethod);

    /// <summary>Gets whether #1102 (nested tuple deconstruction or conditional on the right of a deconstruction) should be avoided.</summary>
    public bool AvoidComplexTupleDeconstruction => _avoidedIssues.Contains(Issue1102ComplexTupleDeconstruction);

    /// <summary>Gets whether #1103 (<c>default(T)</c> of a struct with a conversion operator) should be avoided.</summary>
    public bool AvoidStructConversionDefault => _avoidedIssues.Contains(Issue1103StructConversionDefault);

    /// <summary>Returns <see langword="true"/> if <paramref name="issueNumber"/> is currently avoided.</summary>
    public bool IsAvoided(int issueNumber) => _avoidedIssues.Contains(issueNumber);

    /// <summary>Returns the list of <see cref="KnownIssueEntry"/> items that are actively avoided on this profile.</summary>
    public IReadOnlyList<KnownIssueEntry> GetActiveAvoidedEntries()
    {
        var active = new List<KnownIssueEntry>();
        foreach (var entry in Entries)
        {
            if (_avoidedIssues.Contains(entry.Issue))
            {
                active.Add(entry);
            }
        }

        return active;
    }

    /// <summary>Normalizes a profile string to <c>"M"</c>, <c>"C"</c>, or <c>"G"</c>.</summary>
    public static string NormalizeProfile(string? raw)
    {
        ReadOnlySpan<char> trimmed = (raw ?? "C").AsSpan().Trim();
        if (trimmed.Equals("M", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("MANAGED", StringComparison.OrdinalIgnoreCase))
        {
            return "M";
        }

        if (trimmed.Equals("G", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("CUDA", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("GPU", StringComparison.OrdinalIgnoreCase))
        {
            return "G";
        }

        return "C";
    }

    /// <summary>
    /// Loads <see cref="KnownIssueEntry"/> definitions from <c>known-issues.yml</c>, a YAML list with one mapping per
    /// issue (the keys are described at the top of the file).
    /// Throws an exception if the catalog file cannot be found or parsed.
    /// </summary>
    public static IReadOnlyList<KnownIssueEntry> LoadEntries(string? explicitPath = null)
    {
        string resolvedPath = ResolvePath(explicitPath);
        YamlNode root;
        try
        {
            root = YamlSubsetParser.Parse(File.ReadAllText(resolvedPath, Encoding.UTF8));
        }
        catch (NativeApiReadException ex)
        {
            throw new InvalidDataException($"{resolvedPath}({ex.ToError().Line}): {ex.Message}", ex);
        }

        if (root is not YamlSequence items)
        {
            throw new InvalidDataException($"{resolvedPath}({root.Line}): the file must hold a YAML list of issues.");
        }

        var list = new List<KnownIssueEntry>();
        foreach (var node in items.Items)
        {
            if (node is not YamlMapping item)
            {
                throw new InvalidDataException($"{resolvedPath}({node.Line}): each issue must be a mapping.");
            }

            if (!item.TryGetInt32("issue", out int issueNum))
            {
                throw new InvalidDataException(
                    $"{resolvedPath}({item.Line}): an entry is missing a valid integer 'issue' value.");
            }

            list.Add(new KnownIssueEntry(
                issueNum,
                item.TryGetScalar("kind", out var kind) ? kind : "avoid",
                item.TryGetScalar("summary", out var summary) ? summary : string.Empty,
                ReadStringList(resolvedPath, item, "profiles"),
                ReadStringList(resolvedPath, item, "paths"),
                item.TryGetScalar("avoidedShape", out var avoidedShape) ? avoidedShape : string.Empty,
                ReadStringList(resolvedPath, item, "shapes"),
                item.TryGetScalar("removalCondition", out var removalCondition) ? removalCondition : null,
                ReadStringList(resolvedPath, item, "classifyOnlyPaths") is { Count: > 0 } classifyOnlyPaths
                    ? classifyOnlyPaths
                    : null));
        }

        if (list.Count == 0)
        {
            throw new InvalidDataException($"known-issues.yml at '{resolvedPath}' contains zero entries.");
        }

        return list;
    }

    private static bool AppliesToProfile(KnownIssueEntry entry, string normalizedProfile)
    {
        for (int i = 0; i < entry.Profiles.Count; i++)
        {
            if (string.Equals(entry.Profiles[i], normalizedProfile, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> ReadStringList(string path, YamlMapping item, string key)
    {
        var result = new List<string>();
        if (!item.TryGetValue(key, out var node))
        {
            return result;
        }

        if (node is not YamlSequence values)
        {
            throw new InvalidDataException($"{path}({node.Line}): '{key}' must be a list.");
        }

        foreach (var value in values.Items)
        {
            if (value is YamlScalar { IsNull: false, Value: var text } && !string.IsNullOrWhiteSpace(text))
            {
                result.Add(text.AsSpan().Trim().ToString());
            }
        }

        return result;
    }

    private static string ResolvePath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!File.Exists(explicitPath))
            {
                throw new FileNotFoundException($"Explicit known-issues.yml path not found: '{explicitPath}'.", explicitPath);
            }

            return explicitPath;
        }

        string alongsideExe = Path.Combine(AppContext.BaseDirectory, "known-issues.yml");
        if (File.Exists(alongsideExe))
        {
            return alongsideExe;
        }

        for (string? dir = AppContext.BaseDirectory; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            string candidate = Path.Combine(dir, "tools", "fuzz-diff", "known-issues.yml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        for (string? dir = Directory.GetCurrentDirectory(); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            string candidate = Path.Combine(dir, "tools", "fuzz-diff", "known-issues.yml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate tools/fuzz-diff/known-issues.yml.");
    }
}
