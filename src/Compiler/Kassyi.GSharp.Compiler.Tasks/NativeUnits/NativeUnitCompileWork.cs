namespace Kassyi.GSharp.Compiler.Tasks.NativeUnits;

/// <summary>One unit of one architecture, planned for compilation, with the record to write once it compiled.</summary>
/// <param name="SourcePath">The full path of the C++ source file.</param>
/// <param name="Arch">The target architecture.</param>
/// <param name="Artifacts">The files the compilation produces.</param>
/// <param name="RecordContent">The command record content that marks the object file as current.</param>
internal readonly record struct NativeUnitCompileWork(
    string SourcePath,
    string Arch,
    NativeUnitArtifacts Artifacts,
    string RecordContent);
