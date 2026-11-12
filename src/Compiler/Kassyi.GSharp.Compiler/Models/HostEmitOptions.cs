namespace Kassyi.GSharp.Compiler.Models;

/// <summary>What the host outputs take from the compilation and the build options.</summary>
/// <remarks>
/// Only names: an edit to the compilation leaves the value equal, so the outputs that combine it stay cached.
/// </remarks>
/// <param name="Config">The generator options.</param>
/// <param name="ModuleName">
/// The module name (<see cref="AotModuleNames.GetModuleName"/>), which names the module context class, the
/// fatbin and the native library that the wrappers load.
/// </param>
internal readonly record struct HostEmitOptions(GeneratorConfig Config, string ModuleName);
