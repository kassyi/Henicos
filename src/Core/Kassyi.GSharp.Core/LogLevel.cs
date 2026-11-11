namespace Kassyi.GSharp.Core;

/// <summary>
/// Specifies the severity and verbosity levels for diagnostic and performance logs.
/// </summary>
public enum LogLevel
{
    /// <summary>Detailed diagnostic and tracing information.</summary>
    Trace = 0,

    /// <summary>Verbose execution flow details.</summary>
    Verbose = 1,

    /// <summary>Debug information useful during active development.</summary>
    Debug = 2,

    /// <summary>General informational messages.</summary>
    Info = 3,

    /// <summary>Warning messages indicating non-fatal issues or unexpected conditions.</summary>
    Warning = 4,

    /// <summary>Error messages indicating failures or unhandled exceptions.</summary>
    Error = 5,

    /// <summary>Disables all logging output.</summary>
    None = 6
}
