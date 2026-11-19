namespace Kassyi.GSharp.Compiler.Tasks.NativeUnits;

/// <summary>One native module of one architecture, linked from the objects of its planned units.</summary>
/// <param name="Arch">The target architecture.</param>
/// <param name="UnitObjectPaths">The object files of the module units, in unit order.</param>
/// <param name="OutputDllPath">The path of the linked library.</param>
internal readonly record struct NativeLinkTarget(string Arch, string[] UnitObjectPaths, string OutputDllPath);
