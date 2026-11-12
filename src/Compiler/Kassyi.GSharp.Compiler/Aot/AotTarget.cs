namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>What <see cref="AotArtifactWriter"/> takes from the compilation.</summary>
/// <param name="ModuleName">The module name (<see cref="AotModuleNames.GetModuleName"/>), naming the files.</param>
/// <param name="IsDesignTimeCompilation">Whether an IDE worker runs the generator, which must not write the artifact.</param>
internal readonly record struct AotTarget(string ModuleName, bool IsDesignTimeCompilation)
{
    public static AotTarget Create(Compilation compilation) =>
        new(AotModuleNames.GetModuleName(compilation), AotArtifactWriter.IsDesignTimeCompilation(compilation));
}
