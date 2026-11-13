using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>A record read from a referenced assembly, with what the consumer checks before using it.</summary>
/// <param name="Record">The record.</param>
/// <param name="AssemblyName">The assembly that declares the function or struct.</param>
/// <param name="FormatVersion">The record layout the assembly was built with.</param>
/// <param name="PreludeHash">The hash of the preludes the text was written for.</param>
internal readonly record struct ImportedDeviceRecord(
    DeviceExportRecord Record,
    string AssemblyName,
    int FormatVersion,
    string PreludeHash)
{
    /// <summary>
    /// Whether the text was written by this G#: the same record layout, preludes and internal names
    /// (<see cref="DeviceExportRecord.CurrentPreludeHash"/>).
    /// </summary>
    public bool IsCompatible =>
        FormatVersion == DeviceExportRecord.CurrentFormatVersion &&
        string.Equals(PreludeHash, DeviceExportRecord.CurrentPreludeHash, StringComparison.Ordinal);

    /// <summary>Whether the library's build could not transpile the text of a dialect.</summary>
    public bool FailedFor(bool isCuda) => (isCuda ? Record.Cuda : Record.Cpp).Failed;
}
