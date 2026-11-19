namespace Kassyi.GSharp.Compiler.Tasks.NativeUnits;

/// <summary>One C++ translation unit of a native module: its source file and the stem of its object file.</summary>
/// <param name="SourcePath">The full path of the C++ source file.</param>
/// <param name="UnitStem">The file stem that names the unit object file and its command record.</param>
internal readonly record struct NativeUnitSource(string SourcePath, string UnitStem);
