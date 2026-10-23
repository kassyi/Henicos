namespace Kassyi.GSharp.Compiler.Models;

/// <summary>What the host outputs take from the compilation and the build options.</summary>
/// <remarks>
/// Only names: an edit to the compilation leaves the value equal, so the outputs that combine it stay cached.
/// </remarks>
/// <param name="Config">The generator options.</param>
/// <param name="AssemblyName">The name of the assembly symbol, which the wrappers refer to the module context by.</param>
/// <param name="ModuleAssemblyName">The compilation's assembly name, or <c>UnknownAssembly</c>, which names the module context.</param>
internal readonly record struct HostEmitOptions(GeneratorConfig Config, string AssemblyName, string ModuleAssemblyName)
{
    /// <summary>Takes the two assembly names as the outputs used them before the outputs were split.</summary>
    /// <param name="config">The generator options.</param>
    /// <param name="assemblyName"><c>Compilation.Assembly.Name</c>.</param>
    /// <param name="compilationAssemblyName"><c>Compilation.AssemblyName</c>, which may be <see langword="null"/>.</param>
    public static HostEmitOptions Create(GeneratorConfig config, string assemblyName, string? compilationAssemblyName) =>
        new(config, assemblyName, compilationAssemblyName ?? "UnknownAssembly");
}
