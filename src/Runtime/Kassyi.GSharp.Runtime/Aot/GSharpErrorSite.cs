namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Represents a 1-based or deterministic 31-bit error site recorded by the transpiler (#729).
/// </summary>
/// <param name="SiteId">The deterministic 31-bit site ID.</param>
/// <param name="File">The source file name where the trap expression was evaluated.</param>
/// <param name="Line">The 1-based source line number.</param>
/// <param name="Column">The 1-based source column number.</param>
/// <param name="MemberName">The enclosing kernel or device method name.</param>
/// <param name="DefaultMessage">The base diagnostic message for this site.</param>
public readonly record struct GSharpErrorSite(
    uint SiteId,
    string File,
    int Line,
    int Column,
    string MemberName,
    string DefaultMessage);
