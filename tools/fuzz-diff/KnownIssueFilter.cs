using System.Text.Json;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Describes a single tracked known issue entry loaded from <c>known-issues.json</c>.
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
/// Evaluates per-issue avoidance and classification rules for <see cref="ScalarFuzzGenerator"/> based on <c>known-issues.json</c>,
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

    /// <summary>Issue #1101: <c>??=</c> used as a value has type <see cref="Nullable{T}"/> in generated C++ and does not compile.</summary>
    public const int Issue1101CoalesceAssignValue = 1101;

    /// <summary>Issue #1102: Nested tuple deconstruction and conditional on the right of a deconstruction fail with GSH0003.</summary>
    public const int Issue1102ComplexTupleDeconstruction = 1102;

    /// <summary>Issue #1103: <c>default(T)</c> leaves the fields of a struct with an implicit conversion uninitialized on native and CUDA.</summary>
    public const int Issue1103StructConversionDefault = 1103;

    /// <summary>Issue #1078: MSVC <c>/Og</c> optimizer miscompiles in generated native C++ kernels (including negated dividend division, 11177375).</summary>
    public const int Issue1078MsvcOptimizerMiscompile = 1078;

    /// <summary>Gets the complete list of issue numbers that have an avoidance handler in the generator.</summary>
    public static IReadOnlyList<int> SupportedAvoidanceIssues { get; } =
    [
        Issue1079NarrowIncDecValue,
        Issue998MixedWidthNullCoalesce,
        Issue984NullableLocalVariable,
        Issue645PlainStructInstanceMethod,
        Issue1101CoalesceAssignValue,
        Issue1102ComplexTupleDeconstruction,
        Issue1103StructConversionDefault,
        Issue1078MsvcOptimizerMiscompile,
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

    /// <summary>Gets the catalog of known issue entries loaded from <c>known-issues.json</c>.</summary>
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

    /// <summary>Gets whether #1101 (<c>??=</c> used as a value inside an expression) should be avoided.</summary>
    public bool AvoidCoalesceAssignValue => _avoidedIssues.Contains(Issue1101CoalesceAssignValue);

    /// <summary>Gets whether #1102 (nested tuple deconstruction or conditional on the right of a deconstruction) should be avoided.</summary>
    public bool AvoidComplexTupleDeconstruction => _avoidedIssues.Contains(Issue1102ComplexTupleDeconstruction);

    /// <summary>Gets whether #1103 (<c>default(T)</c> of a struct with a conversion operator) should be avoided.</summary>
    public bool AvoidStructConversionDefault => _avoidedIssues.Contains(Issue1103StructConversionDefault);

    /// <summary>Gets whether #1078 shape 1 (negation followed by division) should be avoided.</summary>
    public bool AvoidMsvcNegatedConstantDivision => _avoidedIssues.Contains(Issue1078MsvcOptimizerMiscompile);

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
    /// Loads <see cref="KnownIssueEntry"/> definitions from <c>known-issues.json</c>.
    /// Throws an exception if the catalog file cannot be found or parsed.
    /// </summary>
    public static IReadOnlyList<KnownIssueEntry> LoadEntries(string? explicitJsonPath = null)
    {
        string resolvedPath = ResolveJsonPath(explicitJsonPath);
        using var stream = File.OpenRead(resolvedPath);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("issues", out var issuesElem) ||
            issuesElem.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"known-issues.json at '{resolvedPath}' is missing an 'issues' array.");
        }

        var list = new List<KnownIssueEntry>();
        foreach (var item in issuesElem.EnumerateArray())
        {
            if (!item.TryGetProperty("issue", out var issueProp) || !issueProp.TryGetInt32(out int issueNum))
            {
                throw new InvalidDataException($"An entry in '{resolvedPath}' is missing a valid integer 'issue' property.");
            }

            string kind = item.TryGetProperty("kind", out var kindProp) ? (kindProp.GetString() ?? "avoid") : "avoid";
            string summary = item.TryGetProperty("summary", out var sumProp) ? (sumProp.GetString() ?? string.Empty) : string.Empty;
            string avoidedShape = item.TryGetProperty("avoidedShape", out var shapeProp) ? (shapeProp.GetString() ?? string.Empty) : string.Empty;
            string? removalCondition = item.TryGetProperty("removalCondition", out var remProp) ? remProp.GetString() : null;
            var profiles = ReadStringArray(item, "profiles");
            var paths = ReadStringArray(item, "paths");
            var shapes = ReadStringArray(item, "shapes");
            var classifyOnlyPaths = ReadStringArray(item, "classifyOnlyPaths");
            list.Add(new KnownIssueEntry(
                issueNum,
                kind,
                summary,
                profiles,
                paths,
                avoidedShape,
                shapes,
                removalCondition,
                classifyOnlyPaths.Count > 0 ? classifyOnlyPaths : null));
        }

        if (list.Count == 0)
        {
            throw new InvalidDataException($"known-issues.json at '{resolvedPath}' contains zero entries.");
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

    private static List<string> ReadStringArray(JsonElement obj, string propertyName)
    {
        var result = new List<string>();
        if (obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in prop.EnumerateArray())
            {
                if (elem.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                {
                    result.Add(s.AsSpan().Trim().ToString());
                }
            }
        }

        return result;
    }

    private static string ResolveJsonPath(string? explicitJsonPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitJsonPath))
        {
            if (!File.Exists(explicitJsonPath))
            {
                throw new FileNotFoundException($"Explicit known-issues.json path not found: '{explicitJsonPath}'.", explicitJsonPath);
            }

            return explicitJsonPath;
        }

        string alongsideExe = Path.Combine(AppContext.BaseDirectory, "known-issues.json");
        if (File.Exists(alongsideExe))
        {
            return alongsideExe;
        }

        for (string? dir = AppContext.BaseDirectory; !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            string candidate = Path.Combine(dir, "tools", "fuzz-diff", "known-issues.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        for (string? dir = Directory.GetCurrentDirectory(); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            string candidate = Path.Combine(dir, "tools", "fuzz-diff", "known-issues.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate tools/fuzz-diff/known-issues.json.");
    }
}
