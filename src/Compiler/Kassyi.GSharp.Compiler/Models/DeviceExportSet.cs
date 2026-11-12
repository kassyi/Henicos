namespace Kassyi.GSharp.Compiler.Models;

/// <summary>The device code export records of one compilation, and the generator's own failures in building them.</summary>
/// <param name="Functions">The records of the device functions.</param>
/// <param name="Structs">The records of the structs, one per struct and method that uses it (written once).</param>
internal readonly record struct DeviceExportSet(
    EquatableArray<DeviceExportRecord> Functions,
    EquatableArray<DeviceExportRecord> Structs);
