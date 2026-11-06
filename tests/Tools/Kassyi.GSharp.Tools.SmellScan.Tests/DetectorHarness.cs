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

    /// <summary>Builds a warning rule for <paramref name="detector"/> that sets its threshold parameter.</summary>
    internal static SmellRule Rule(ISmellDetector detector, int threshold) =>
        new(
            "test-rule",
            detector,
            SmellSeverity.Warning,
            ImmutableDictionary<string, int>.Empty.Add(detector.ThresholdParameter, threshold),
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
        var parameters = new RuleParameters(rule.Parameters);
        var symbols = new List<string>();
        foreach (var finding in ThresholdJudge.Judge(rule, parameters, Measure(detector, caseFile)))
        {
            symbols.Add(finding.Measurement.Symbol);
        }

        return symbols;
    }
}
