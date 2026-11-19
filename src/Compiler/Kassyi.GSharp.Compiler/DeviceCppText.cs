namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The C++ text of a device function in the native module: its state machine, followed by its native stack form when it
/// has one.
/// </summary>
internal readonly record struct DeviceCppText(
    string Signature,
    string Body,
    HashSet<string> Includes,
    EquatableArray<ErrorSiteEntry> ErrorSites);
