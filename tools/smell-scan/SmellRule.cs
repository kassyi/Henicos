using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>One rule of a smells file.</summary>
/// <param name="Id">The kebab-case name of the rule.</param>
/// <param name="Detector">The detector that measures for this rule.</param>
/// <param name="Severity">The severity of its findings.</param>
/// <param name="Parameters">The parameters the rule sets itself; others take the detector default.</param>
/// <param name="Include">Globs of the files the rule looks at; empty means every file.</param>
/// <param name="Exclude">Globs of the files the rule skips.</param>
/// <param name="Overrides">Parameter changes for some files; the last one that matches wins.</param>
/// <param name="Suggestion">The advice shown with a finding.</param>
/// <param name="Help">A pointer to the guidance on the smell; empty when the rule has none.</param>
/// <param name="Line">The 1-based line where the rule starts in the file.</param>
internal sealed record SmellRule(
    string Id,
    ISmellDetector Detector,
    SmellSeverity Severity,
    ImmutableDictionary<string, int> Parameters,
    ImmutableArray<string> Include,
    ImmutableArray<string> Exclude,
    ImmutableArray<ParameterOverride> Overrides,
    string Suggestion,
    string Help,
    int Line);
