using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>Everything a report writer needs. It holds results only; writers decide nothing.</summary>
/// <param name="Commit">The commit of the checkout, or <see langword="null"/> outside a git checkout.</param>
/// <param name="ConfigPath">The configuration file, relative to the root when it is inside it.</param>
/// <param name="ConfigHash">The hash of the configuration text, with line endings ignored.</param>
/// <param name="Files">The number of files that were measured.</param>
/// <param name="ExcludedGenerated">The number of files left out because they are generated.</param>
/// <param name="Tier">The input the enabled rules needed.</param>
/// <param name="RuleIds">The ids of the rules that ran, in ordinal order.</param>
/// <param name="Findings">The findings in report order, with fingerprints.</param>
/// <param name="Unmeasured">The files with conditional branches that no symbol set made active, by path.</param>
/// <param name="ProjectByPath">The project directory of each file that has a finding.</param>
/// <param name="Rules">The rules that ran, in ordinal order of their ids.</param>
/// <param name="Metrics">The largest measurements of each rule, when the run was asked to list them; otherwise empty.</param>
internal sealed record ScanReport(
    string? Commit,
    string ConfigPath,
    string ConfigHash,
    int Files,
    int ExcludedGenerated,
    string Tier,
    ImmutableArray<string> RuleIds,
    ImmutableArray<Finding> Findings,
    ImmutableArray<UnmeasuredBranches> Unmeasured,
    ImmutableDictionary<string, string> ProjectByPath,
    ImmutableArray<RuleInfo> Rules,
    ImmutableArray<RuleMetric> Metrics);
