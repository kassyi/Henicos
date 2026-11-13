namespace Kassyi.GSharp.Compiler.ErrorSites;

/// <summary>
/// Represents a deterministic 31-bit compile-time error site entry recorded for device and native CPU runtime trap reporting (#729).
/// </summary>
internal readonly record struct ErrorSiteEntry(
    uint SiteId,
    string File,
    int Line,
    int Column,
    string MemberName,
    string DefaultMessage);
