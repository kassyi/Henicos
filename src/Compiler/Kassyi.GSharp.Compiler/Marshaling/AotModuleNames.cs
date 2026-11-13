namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// The names that come from the module of an assembly: the module name itself, the file names of its native artifacts
/// and the name of the module context class that loads its fatbin. The generator, the artifact writer and the launchers
/// all go through this class, so that they agree on one module name.
/// </summary>
internal static class AotModuleNames
{
    /// <summary>The prefix of the file names of the native artifacts of one assembly.</summary>
    public const string ArtifactFilePrefix = "Kassyi.GSharp.Kernels_";

    /// <summary>The module name of a compilation without an assembly name.</summary>
    public const string UnknownModuleName = "UnknownAssembly";

    /// <summary>
    /// The module name of <paramref name="compilation"/>: its assembly name, or <c>UnknownAssembly</c> without one. The
    /// artifact files, the native library and the module context class are all named after it.
    /// </summary>
    public static string GetModuleName(Compilation compilation) => compilation.AssemblyName ?? UnknownModuleName;

    /// <summary>
    /// The file name, without extension, of the native artifacts of a module (<c>.g.cu</c>, <c>.g.cpp</c>, the fatbin
    /// and the native library): <c>Kassyi.GSharp.Kernels_</c> and the module name escaped as in
    /// <see cref="GetModuleContextName"/> (<c>Henicos.Benchmarks</c> gives
    /// <c>Kassyi.GSharp.Kernels_Henicos_x00002eBenchmarks</c>), so that two assemblies in one output directory never
    /// share a native library or fatbin. The native build names its outputs after these source files.
    /// </summary>
    public static string GetArtifactFileStem(string moduleName) =>
        ArtifactFilePrefix + GSharpNameEncoding.EscapeName(moduleName);

    /// <summary>
    /// The name of the module context class of a module: <c>GSharpModuleContext_</c> and the module name escaped as the
    /// emitted C++ names are (<see cref="GSharpNameEncoding.GetModuleContextName"/>): ASCII letters and digits stay,
    /// <c>_</c> becomes <c>_u</c> and any other character <c>_x</c> and six hex digits of its code point
    /// (<c>My.Lib</c> gives <c>GSharpModuleContext_My_x00002eLib</c>). Two names give two classes, and the demangle
    /// tool recovers the name.
    /// </summary>
    public static string GetModuleContextName(string moduleName) => GSharpNameEncoding.GetModuleContextName(moduleName);

    /// <summary>
    /// The static field of a class that holds the bytes of the external module <paramref name="moduleName"/>
    /// (<c>[CudaImport]</c>, YAML <c>moduleName</c>): <c>__gsharp_module_</c> and the module name escaped by
    /// <see cref="GSharpNameEncoding.EscapeName"/>, which is injective, so <c>a.gfatbin</c> and <c>a_gfatbin</c> get
    /// fields of their own. Both generators name the field this way: the methods of one module in one class share one
    /// field, which a YAML binding of the class declares when it binds that module.
    /// </summary>
    public static string GetModuleFieldName(string moduleName) => GSharpNameEncoding.GetModuleFieldName(moduleName);
}
