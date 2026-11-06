namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>The form a report is written in.</summary>
internal enum ReportFormat
{
    /// <summary>The tool's own JSON, grouped by file.</summary>
    Json,

    /// <summary>SARIF 2.1.0.</summary>
    Sarif,

    /// <summary>Plain text.</summary>
    Text,
}
