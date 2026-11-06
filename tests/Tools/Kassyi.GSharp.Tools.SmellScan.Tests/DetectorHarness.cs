#nullable enable
using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan.Tests;

/// <summary>Runs one detector over a test case file, with or without a threshold.</summary>
internal static class DetectorHarness
{
    /// <summary>Measures <paramref name="caseFile"/> with the first (no symbols defined) parse.</summary>
    internal static IReadOnlyList<Measurement> Measure(ISmellDetector detector, string caseFile)
    {
        var parsed = SyntaxInput.Parse(caseFile, TestCaseFiles.Read(caseFile));
        var sink = new MeasurementSink();
        detector.Measure(parsed.Variants[0], sink);
        return sink.Items;
    }

    /// <summary>Returns the measurement of <paramref name="caseFile"/> whose symbol is <paramref name="symbol"/>.</summary>
    internal static Measurement Single(ISmellDetector detector, string caseFile, string symbol)
    {
        var matches = new List<Measurement>();
        foreach (var measurement in Measure(detector, caseFile))
        {
            if (measurement.Symbol == symbol)
            {
                matches.Add(measurement);
            }
        }

        return Assert.Single(matches);
    }

    /// <summary>Returns the measurement of <paramref name="caseFile"/> with <paramref name="symbol"/> and <paramref name="metric"/>.</summary>
    internal static Measurement Single(ISmellDetector detector, string caseFile, string symbol, string metric)
    {
        var matches = new List<Measurement>();
        foreach (var measurement in Measure(detector, caseFile))
        {
            if (measurement.Symbol == symbol && measurement.Metric == metric)
            {
                matches.Add(measurement);
            }
        }

        return Assert.Single(matches);
    }

    /// <summary>Counts the measurements of <paramref name="caseFile"/> whose symbol is <paramref name="symbol"/>.</summary>
    internal static int Count(ISmellDetector detector, string caseFile, string symbol)
    {
        var count = 0;
        foreach (var measurement in Measure(detector, caseFile))
        {
            if (measurement.Symbol == symbol)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Judges <paramref name="caseFile"/> with the given parameter values; the rest take the detector defaults.</summary>
    internal static List<Finding> Judge(ISmellDetector detector, string caseFile, params (string Name, int Value)[] values)
    {
        var map = new Dictionary<string, int>();
        foreach (var parameter in detector.Parameters)
        {
            map[parameter.Name] = parameter.Default;
        }

        foreach (var (name, value) in values)
        {
            map[name] = value;
        }

        var rule = new SmellRule(
            "test-rule",
            detector,
            SmellSeverity.Warning,
            ImmutableDictionary<string, int>.Empty,
            [],
            [],
            [],
            "advice",
            string.Empty,
            1);
        return ThresholdJudge.Judge(rule, new RuleParameters(map), Measure(detector, caseFile));
    }

    /// <summary>Builds a warning rule for <paramref name="detector"/> that sets its first parameter, the threshold.</summary>
    internal static SmellRule Rule(ISmellDetector detector, int threshold) =>
        new(
            "test-rule",
            detector,
            SmellSeverity.Warning,
            ImmutableDictionary<string, int>.Empty.Add(detector.Parameters[0].Name, threshold),
            [],
            [],
            [],
            "advice",
            string.Empty,
            1);

    /// <summary>Judges <paramref name="caseFile"/> with <paramref name="threshold"/> and returns the symbols that exceed it.</summary>
    internal static List<string> ExceedingSymbols(ISmellDetector detector, string caseFile, int threshold)
    {
        var rule = Rule(detector, threshold);
        var values = new Dictionary<string, int>();
        foreach (var declared in detector.Parameters)
        {
            values[declared.Name] = declared.Default;
        }

        foreach (var pair in rule.Parameters)
        {
            values[pair.Key] = pair.Value;
        }

        var parameters = new RuleParameters(values);
        var symbols = new List<string>();
        foreach (var finding in ThresholdJudge.Judge(rule, parameters, Measure(detector, caseFile)))
        {
            symbols.Add(finding.Measurement.Symbol);
        }

        return symbols;
    }
}
