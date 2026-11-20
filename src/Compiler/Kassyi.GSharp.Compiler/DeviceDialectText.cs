namespace Kassyi.GSharp.Compiler;

/// <summary>The text of a device function in one dialect, as the transpiler wrote it, with its failures.</summary>
internal readonly record struct DeviceDialectText(
    FailureCollector Failures,
    string Signature,
    string Source,
    HashSet<string> Includes,
    StructSourceCollector Structs);
