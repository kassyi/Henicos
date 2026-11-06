namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>What a report lists.</summary>
internal enum ReportMode
{
    /// <summary>The measurements above the threshold of their rule.</summary>
    Findings,

    /// <summary>The largest measurements of each rule, above the threshold or not.</summary>
    Metrics,
}
