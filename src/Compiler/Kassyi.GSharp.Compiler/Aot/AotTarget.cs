namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>What <see cref="AotArtifactWriter"/> takes from the compilation.</summary>
/// <param name="AssemblyName">The compilation's assembly name, or <c>UnknownAssembly</c>, which names the artifact.</param>
/// <param name="IsDesignTimeCompilation">Whether an IDE worker runs the generator, which must not write the artifact.</param>
internal readonly record struct AotTarget(string AssemblyName, bool IsDesignTimeCompilation)
{
    public static AotTarget Create(Compilation compilation) =>
        new(compilation.AssemblyName ?? "UnknownAssembly", AotArtifactWriter.IsDesignTimeCompilation(compilation));
}
