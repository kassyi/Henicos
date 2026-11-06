using System.Collections.Immutable;
using System.Diagnostics;
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
    private readonly int _metricsTop;

    /// <summary>Creates a runner for the repository at <paramref name="root"/>.</summary>
    /// <param name="root">The repository root.</param>
    /// <param name="config">The rules.</param>
    /// <param name="parallelism">The number of files measured at once.</param>
    /// <param name="metricsTop">When above 0, the report lists this many largest measurements of each rule.</param>
    public ScanRunner(string root, SmellConfig config, int parallelism, int metricsTop = 0)
    {
        _root = root;
        _config = config;
        _scope = new RuleScope(config);
        _parallelism = Math.Max(1, parallelism);
        _metricsTop = metricsTop;
    }

    /// <summary>Gets the time the last run spent parsing and measuring files.</summary>
    public TimeSpan MeasureTime { get; private set; }

    /// <summary>Gets the time the last run spent merging, judging and ordering what was measured.</summary>
    public TimeSpan JudgeTime { get; private set; }

    /// <summary>Measures <paramref name="files"/> and returns the report.</summary>
    /// <param name="files">The repository-relative paths to look at.</param>
    /// <param name="configPath">The path of the configuration to show in the report.</param>
    /// <param name="configText">The text of the configuration, for its hash.</param>
    public ScanReport Run(ImmutableArray<string> files, string configPath, string configText)
    {
        var clock = Stopwatch.StartNew();
        var candidates = SelectCandidates(files, out var excludedByName);
        var results = new FileResult[candidates.Count];
        var options = new ParallelOptions { MaxDegreeOfParallelism = _parallelism };
        Parallel.For(0, candidates.Count, options, i => results[i] = ScanFile(candidates[i]));
        MeasureTime = clock.Elapsed;

        clock.Restart();
        var gathered = Gather(candidates, results);
        var locator = new ProjectLocator(_root);
        var units = Units(gathered.Measured, locator);
        var fingerprinted = FindingFingerprint.Assign(Judge(units));
        var report = new ScanReport(
            CurrentCommit(),
            configPath,
            HashIgnoringLineEndings(configText),
            gathered.Scanned,
            excludedByName + gathered.Generated,
            "syntax",
            ActiveRuleIds(),
            [.. fingerprinted],
            [.. gathered.Unmeasured],
            ProjectsOf(fingerprinted, locator),
            ActiveRules(),
            _metricsTop > 0 ? TopMetrics(units) : []);
        JudgeTime = clock.Elapsed;
        return report;
    }

    private List<string> SelectCandidates(ImmutableArray<string> files, out int excludedByName)
    {
        excludedByName = 0;
        var candidates = new List<string>(files.Length);
        foreach (var path in files)
        {
            if (GeneratedCodeFilter.HasGeneratedName(path))
            {
                excludedByName++;
            }
            else if (!_scope.IsOutOfScope(path))
            {
                candidates.Add(path);
            }
        }

        return candidates;
    }

    private static Gathered Gather(List<string> candidates, FileResult[] results)
    {
        var measured = new List<Candidate>();
        var unmeasured = new List<UnmeasuredBranches>();
        var scanned = 0;
        var generated = 0;
        for (var i = 0; i < results.Length; i++)
        {
            var result = results[i];
            if (result.IsGenerated)
            {
                generated++;
                continue;
            }

            scanned++;
            if (!result.UnmeasuredLines.IsEmpty)
            {
                unmeasured.Add(new UnmeasuredBranches(candidates[i], result.UnmeasuredLines));
            }

            measured.AddRange(result.Candidates);
        }

        unmeasured.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));
        return new Gathered(measured, unmeasured, scanned, generated);
    }

    private static List<Finding> Judge(List<MergedUnit> units)
    {
        var findings = new List<Finding>();
        foreach (var unit in units)
        {
            if (ThresholdJudge.JudgeOne(unit.Rule, unit.Parameters, unit.Measurement, unit.Related) is { } finding)
            {
                findings.Add(finding);
            }
        }

        findings.Sort(FindingOrder.Instance);
        return findings;
    }

    private static ImmutableDictionary<string, string> ProjectsOf(List<Finding> findings, ProjectLocator locator)
    {
        var projects = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            var path = finding.Measurement.Path;
            if (!projects.ContainsKey(path))
            {
                projects.Add(path, locator.Find(path));
            }
        }

        return projects.ToImmutable();
    }

    private List<MergedUnit> Units(List<Candidate> measured, ProjectLocator locator)
    {
        var units = PartialTypeMerger.Merge(measured, locator, _scope);
        foreach (var candidate in measured)
        {
            if (candidate.Measurement.Group is null)
            {
                units.Add(new MergedUnit(
                    candidate.Rule,
                    candidate.Measurement,
                    _scope.ParametersFor(candidate.Rule, candidate.Measurement.Path),
                    []));
            }
        }

        return units;
    }

    private ImmutableArray<RuleMetric> TopMetrics(List<MergedUnit> units)
    {
        var all = new List<RuleMetric>(units.Count);
        foreach (var unit in units)
        {
            all.Add(new RuleMetric(
                unit.Rule.Id,
                unit.Measurement,
                unit.Rule.Detector.ThresholdOf(unit.Measurement, unit.Parameters),
                unit.Rule.Detector.Exceeds(unit.Measurement, unit.Parameters)));
        }

        all.Sort(CompareMetrics);
        var taken = new Dictionary<string, int>(StringComparer.Ordinal);
        var top = new List<RuleMetric>();
        foreach (var metric in all)
        {
            var key = metric.RuleId + "|" + metric.Measurement.Metric;
            taken.TryGetValue(key, out var count);
            if (count < _metricsTop)
            {
                taken[key] = count + 1;
                top.Add(metric);
            }
        }

        return [.. top];
    }

    // By rule, then metric, then the largest value first; equal values are told apart by their place.
    private static int CompareMetrics(RuleMetric x, RuleMetric y)
    {
        var byRule = string.CompareOrdinal(x.RuleId, y.RuleId);
        if (byRule != 0)
        {
            return byRule;
        }

        var byMetric = string.CompareOrdinal(x.Measurement.Metric, y.Measurement.Metric);
        if (byMetric != 0)
        {
            return byMetric;
        }

        var byValue = y.Measurement.Value.CompareTo(x.Measurement.Value);
        if (byValue != 0)
        {
            return byValue;
        }

        var byPath = string.CompareOrdinal(x.Measurement.Path, y.Measurement.Path);
        if (byPath != 0)
        {
            return byPath;
        }

        var byLine = x.Measurement.Range.StartLine.CompareTo(y.Measurement.Range.StartLine);
        return byLine != 0 ? byLine : x.Measurement.Range.StartColumn.CompareTo(y.Measurement.Range.StartColumn);
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
        var best = new Dictionary<UnitKey, Candidate>();
        foreach (var variant in parsed.Variants)
        {
            MeasureVariant(variant, rules, best);
        }

        var kept = new List<Candidate>(best.Values);
        kept.Sort(CompareCandidates);
        return new FileResult(false, kept, parsed.UnmeasuredBranchLines);
    }

    private void MeasureVariant(DetectorContext variant, List<SmellRule> rules, Dictionary<UnitKey, Candidate> best)
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
            foreach (var measurement in measurements)
            {
                // A unit spread over files is judged after its parts are added up, and the metrics listing needs every
                // measurement; otherwise a measurement that is not above its threshold is dropped here.
                var needed = measurement.Group is not null
                    || _metricsTop > 0
                    || rule.Detector.Exceeds(measurement, parameters);
                if (!needed)
                {
                    continue;
                }

                // The same unit can be reached through several symbol sets, and its range, symbol or value can differ between
                // them; the unit is identified by its name position and the largest measurement is kept.
                var key = new UnitKey(rule.Id, measurement.Metric, measurement.UnitPosition);
                if (!best.TryGetValue(key, out var existing) || measurement.Value > existing.Measurement.Value)
                {
                    best[key] = new Candidate(rule, measurement);
                }
            }
        }
    }

    private static int CompareCandidates(Candidate x, Candidate y)
    {
        var byLine = x.Measurement.Range.StartLine.CompareTo(y.Measurement.Range.StartLine);
        if (byLine != 0)
        {
            return byLine;
        }

        var byColumn = x.Measurement.Range.StartColumn.CompareTo(y.Measurement.Range.StartColumn);
        if (byColumn != 0)
        {
            return byColumn;
        }

        var byRule = string.CompareOrdinal(x.Rule.Id, y.Rule.Id);
        return byRule != 0 ? byRule : string.CompareOrdinal(x.Measurement.Metric, y.Measurement.Metric);
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

    private ImmutableArray<RuleInfo> ActiveRules()
    {
        var infos = new List<RuleInfo>();
        foreach (var rule in _config.Rules)
        {
            if (rule.Severity == SmellSeverity.Off)
            {
                continue;
            }

            var values = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var parameter in rule.Detector.Parameters)
            {
                values[parameter.Name] = parameter.Default;
            }

            foreach (var pair in rule.Parameters)
            {
                values[pair.Key] = pair.Value;
            }

            infos.Add(new RuleInfo(
                rule.Id,
                rule.Detector.Name,
                rule.Detector.Description,
                rule.Severity,
                rule.Suggestion,
                rule.Help,
                [.. values]));
        }

        infos.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        return [.. infos];
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

    private sealed record Gathered(List<Candidate> Measured, List<UnmeasuredBranches> Unmeasured, int Scanned, int Generated);

    private readonly record struct UnitKey(string RuleId, string Metric, int UnitPosition);

    private sealed record FileResult(bool IsGenerated, List<Candidate> Candidates, ImmutableArray<int> UnmeasuredLines);
}
