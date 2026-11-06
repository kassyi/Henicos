namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>How serious the findings of a rule are; the names are the SARIF levels.</summary>
internal enum SmellSeverity
{
    /// <summary>The rule does not run.</summary>
    Off,

    /// <summary>A hint for the reviewer.</summary>
    Note,

    /// <summary>Worth a look in review.</summary>
    Warning,

    /// <summary>Must be handled before merging.</summary>
    Error,
}
