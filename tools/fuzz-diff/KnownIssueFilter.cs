using System.Text.Json;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Describes a single tracked known issue entry loaded from <c>known-issues.json</c>.
/// </summary>
public sealed record KnownIssueEntry(
    int Issue,
    string Summary,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<string> Paths,
    string AvoidedShape);

/// <summary>
/// Evaluates per-issue avoidance switches for <see cref="ScalarFuzzGenerator"/> based on <c>known-issues.json</c>,
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

    /// <summary>Issue #877: Binding a C++ reference to a misaligned field of a <c>Pack = 1</c> struct triggers a CUDA misaligned address fault.</summary>
    public const int Issue877PackedStructFieldRefBinding = 877;

    /// <summary>Issue #645: Instance methods of plain structs called from device code are not emitted or diagnosed.</summary>
    public const int Issue645PlainStructInstanceMethod = 645;

    private static readonly IReadOnlyList<KnownIssueEntry> s_defaultEntries =
    [
        new KnownIssueEntry(
            Issue1079NarrowIncDecValue,
            "8/16-bit integer and char increment/decrement expression value returns widened int or post-increment value",
            ["M", "C", "G"],
            ["Native", "Cuda"],
            "Pre/post increment or decrement (++ / --) on sbyte, byte, short, ushort, or char in value-consuming expression contexts; only statement-level increment/decrement is emitted when avoided."),
        new KnownIssueEntry(
            Issue998MixedWidthNullCoalesce,
            "Null-coalescing operator (??) between Nullable<T> of different widths or narrow Nullable<T> implicit promotion",
            ["M", "C", "G"],
            ["Native", "Cuda"],
            "Null-coalescing operator (??) where left and right operands have different underlying types or widths, or where a narrow Nullable<T> is implicitly promoted."),
        new KnownIssueEntry(
            Issue984NullableLocalVariable,
            "Nullable<T> local variable declarations fail C++ / CUDA transpilation or runtime lowering",
            ["C", "G"],
            ["Native", "Cuda"],
            "Local variable declarations of type Nullable<T> (T?); Nullable<T> is instead exercised via struct fields on profiles C and G."),
        new KnownIssueEntry(
            Issue877PackedStructFieldRefBinding,
            "Binding a C++ reference to a misaligned field of a Pack = 1 struct triggers CUDA misaligned address fault",
            ["G"],
            ["Cuda"],
            "Passing a misaligned field of a [StructLayout(LayoutKind.Sequential, Pack = 1)] struct to ref/out/in parameters, ref ternary, tuple deconstruction target, compound assignment, or ??= target."),
        new KnownIssueEntry(
            Issue645PlainStructInstanceMethod,
            "Instance methods of plain structs called from device code are not emitted or diagnosed",
            ["C", "G"],
            ["Native", "Cuda"],
            "Calling plain struct instance methods directly from kernels; computed readonly properties and [GpuDeviceFunction] helpers are used when avoided."),
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

            if (AppliesToProfile(entry, Profile))
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

    /// <summary>Gets whether #877 (binding references to misaligned fields of <c>Pack = 1</c> structs) should be avoided.</summary>
    public bool AvoidPackedStructFieldRefBinding => _avoidedIssues.Contains(Issue877PackedStructFieldRefBinding);

    /// <summary>Gets whether #645 (calling plain struct instance methods from device code) should be avoided.</summary>
    public bool AvoidPlainStructInstanceMethods => _avoidedIssues.Contains(Issue645PlainStructInstanceMethod);

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
    public static string NormalizeProfile(string? raw) => (raw ?? "C").Trim().ToUpperInvariant() switch
    {
        "M" or "MANAGED" => "M",
        "G" or "ALL" or "CUDA" or "GPU" => "G",
        _ => "C",
    };

    /// <summary>Loads <see cref="KnownIssueEntry"/> definitions from <c>known-issues.json</c>, falling back to embedded defaults if not found.</summary>
    public static IReadOnlyList<KnownIssueEntry> LoadEntries(string? explicitJsonPath = null)
    {
        string? resolvedPath = ResolveJsonPath(explicitJsonPath);
        if (resolvedPath is null || !File.Exists(resolvedPath))
        {
            return s_defaultEntries;
        }

        try
        {
            using var stream = File.OpenRead(resolvedPath);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("issues", out var issuesElem) ||
                issuesElem.ValueKind != JsonValueKind.Array)
            {
                return s_defaultEntries;
            }

            var list = new List<KnownIssueEntry>();
            foreach (var item in issuesElem.EnumerateArray())
            {
                if (!item.TryGetProperty("issue", out var issueProp) || !issueProp.TryGetInt32(out int issueNum))
                {
                    continue;
                }

                string summary = item.TryGetProperty("summary", out var sumProp) ? (sumProp.GetString() ?? string.Empty) : string.Empty;
                string avoidedShape = item.TryGetProperty("avoidedShape", out var shapeProp) ? (shapeProp.GetString() ?? string.Empty) : string.Empty;
                var profiles = ReadStringArray(item, "profiles");
                var paths = ReadStringArray(item, "paths");
                list.Add(new KnownIssueEntry(issueNum, summary, profiles, paths, avoidedShape));
            }

            return list.Count > 0 ? list : s_defaultEntries;
        }
        catch (Exception)
        {
            return s_defaultEntries;
        }
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
                    result.Add(s.Trim());
                }
            }
        }

        return result;
    }

    private static string? ResolveJsonPath(string? explicitJsonPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitJsonPath) && File.Exists(explicitJsonPath))
        {
            return explicitJsonPath;
        }

        string alongsideExe = Path.Combine(AppContext.BaseDirectory, "known-issues.json");
        if (File.Exists(alongsideExe))
        {
            return alongsideExe;
        }

        string inCwd = Path.Combine(Directory.GetCurrentDirectory(), "tools", "fuzz-diff", "known-issues.json");
        if (File.Exists(inCwd))
        {
            return inCwd;
        }

        return null;
    }
}

