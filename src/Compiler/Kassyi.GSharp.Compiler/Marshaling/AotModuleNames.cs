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
    /// and the native library): <c>Kassyi.GSharp.Kernels_</c> and the module name with each <c>.</c> replaced by
    /// <c>_</c>.
    /// </summary>
    public static string GetArtifactFileStem(string moduleName) =>
        ArtifactFilePrefix + ReplaceWithUnderscore(moduleName, static c => c != '.');

    /// <summary>
    /// The name of the module context class of a module: <c>GSharpModuleContext_</c> and the module name escaped as the
    /// emitted C++ names are (<see cref="GSharpNameEncoding.GetModuleContextName"/>): ASCII letters and digits stay,
    /// <c>_</c> becomes <c>_u</c> and any other character <c>_x</c> and six hex digits of its code point
    /// (<c>My.Lib</c> gives <c>GSharpModuleContext_My_x00002eLib</c>). Two names give two classes, and the demangle
    /// tool recovers the name.
    /// </summary>
    public static string GetModuleContextName(string moduleName) => GSharpNameEncoding.GetModuleContextName(moduleName);

    // <name> with each character that keeps rejects replaced by '_'; the input itself when keeps accepts them all
    private static string ReplaceWithUnderscore(string name, Func<char, bool> keeps)
    {
        var first = 0;
        while (first < name.Length && keeps(name[first]))
        {
            first++;
        }

        if (first == name.Length)
        {
            return name;
        }

        var chars = name.ToCharArray();
        for (var i = first; i < chars.Length; i++)
        {
            if (!keeps(chars[i]))
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
