namespace Kassyi.GSharp.Compiler.Tasks.NativeUnits;

/// <summary>The intermediate files that compiling one unit for one architecture produces.</summary>
/// <param name="ObjPath">The object file path.</param>
/// <param name="RecordPath">The incremental build command record path next to the object file.</param>
/// <param name="VcPdbPath">The per-unit program database path, or <c>null</c> when none is written.</param>
internal readonly record struct NativeUnitArtifacts(string ObjPath, string RecordPath, string? VcPdbPath);
