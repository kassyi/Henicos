using System.Collections.Immutable;
using Kassyi.GSharp.Hashing;

namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>
/// Runs the rules of a configuration over a set of files. Files are measured in parallel, each into its own slot,
/// and merged in file order, so the report is the same for any degree of parallelism.
/// </summary>
internal sealed class ScanRunner
{
    private readonly string _root;
    private readonly SmellConfig _config;
    private readonly RuleScope _scope;
    private readonly int _parallelism;

    /// <summary>Creates a runner for the repository at <paramref name="root"/>.</summary>
    public ScanRunner(string root, SmellConfig config, int parallelism)
    {
        _root = root;
        _config = config;
        _scope = new RuleScope(config);
        _parallelism = Math.Max(1, parallelism);
    }

    /// <summary>Measures <paramref name="files"/> and returns the report.</summary>
    /// <param name="files">The repository-relative paths to look at.</param>
    /// <param name="configPath">The path of the configuration to show in the report.</param>
    /// <param name="configText">The text of the configuration, for its hash.</param>
    public ScanReport Run(ImmutableArray<string> files, string configPath, string configText)
    {
        var excludedGenerated = 0;
        var candidates = new List<string>(files.Length);
        foreach (var path in files)
        {
            if (GeneratedCodeFilter.HasGeneratedName(path))
            {
                excludedGenerated++;
            }
            else if (!_scope.IsOutOfScope(path))
            {
                candidates.Add(path);
            }
        }

        var results = new FileResult[candidates.Count];
        var options = new ParallelOptions { MaxDegreeOfParallelism = _parallelism };
        Parallel.For(0, candidates.Count, options, i => results[i] = ScanFile(candidates[i]));

        var findings = new List<Finding>();
        var unmeasured = new List<UnmeasuredBranches>();
        var scanned = 0;
        for (var i = 0; i < results.Length; i++)
        {
            var result = results[i];
            if (result.IsGenerated)
            {
                excludedGenerated++;
                continue;
            }

            scanned++;
            if (!result.UnmeasuredLines.IsEmpty)
            {
                unmeasured.Add(new UnmeasuredBranches(candidates[i], result.UnmeasuredLines));
            }

            findings.AddRange(result.Findings);
        }

        unmeasured.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));
        findings.Sort(FindingOrder.Instance);
        var fingerprinted = FindingFingerprint.Assign(findings);

        var locator = new ProjectLocator(_root);
        var projects = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var finding in fingerprinted)
        {
            var path = finding.Measurement.Path;
            if (!projects.ContainsKey(path))
            {
                projects.Add(path, locator.Find(path));
            }
        }

        return new ScanReport(
            CurrentCommit(),
            configPath,
            HashIgnoringLineEndings(configText),
            scanned,
            excludedGenerated,
            "syntax",
            ActiveRuleIds(),
            [.. fingerprinted],
            [.. unmeasured],
            projects.ToImmutable());
    }

    private string? CurrentCommit() =>
        GitRunner.TryRun(_root, "rev-parse", "HEAD") is { } output ? output.AsSpan().Trim().ToString() : null;

    private FileResult ScanFile(string path)
    {
        var text = File.ReadAllText(Path.Combine(_root, path));
        var parsed = SyntaxInput.Parse(path, text);
        if (parsed.IsGenerated)
        {
            return new FileResult(true, [], []);
        }

        var rules = new List<SmellRule>(_scope.Rules(path));
        var best = new Dictionary<FindingKey, Finding>();
        foreach (var variant in parsed.Variants)
        {
            MeasureVariant(variant, rules, best);
        }

        var findings = new List<Finding>(best.Values);
        findings.Sort(FindingOrder.Instance);
        return new FileResult(false, findings, parsed.UnmeasuredBranchLines);
    }

    private void MeasureVariant(DetectorContext variant, List<SmellRule> rules, Dictionary<FindingKey, Finding> best)
    {
        var measured = new Dictionary<ISmellDetector, IReadOnlyList<Measurement>>();
        foreach (var rule in rules)
        {
            if (!measured.TryGetValue(rule.Detector, out var measurements))
            {
                var sink = new MeasurementSink();
                rule.Detector.Measure(variant, sink);
                measurements = sink.Items;
                measured.Add(rule.Detector, measurements);
            }

            var parameters = _scope.ParametersFor(rule, variant.Path);
            foreach (var finding in ThresholdJudge.Judge(rule, parameters, measurements))
            {
                // The same unit can be reached through several symbol sets, and its range, symbol or value can differ between
                // them; the unit is identified by its name position and the largest measurement is kept.
                var key = new FindingKey(finding.RuleId, finding.Measurement.UnitPosition);
                if (!best.TryGetValue(key, out var existing) || finding.Measurement.Value > existing.Measurement.Value)
                {
                    best[key] = finding;
                }
            }
        }
    }

    private ImmutableArray<string> ActiveRuleIds()
    {
        var ids = new List<string>();
        foreach (var rule in _config.Rules)
        {
            if (rule.Severity != SmellSeverity.Off)
            {
                ids.Add(rule.Id);
            }
        }

        ids.Sort(StringComparer.Ordinal);
        return [.. ids];
    }

    private static string HashIgnoringLineEndings(string text)
    {
        var hash = new Fnv1A64();
        foreach (var c in text)
        {
            if (c != '\r')
            {
                hash.Append(c);
            }
        }

        return hash.Value.ToString("x16");
    }

    private readonly record struct FindingKey(string RuleId, int UnitPosition);

    private sealed record FileResult(bool IsGenerated, List<Finding> Findings, ImmutableArray<int> UnmeasuredLines);
}
