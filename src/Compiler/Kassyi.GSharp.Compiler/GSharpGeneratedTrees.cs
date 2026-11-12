using Kassyi.GSharp.Compiler.NativeApi;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Tells the syntax trees that the generators of this assembly added from the user's own and other generated code.
/// </summary>
/// <remarks>
/// The host rules analyze generated code too, since another generator's output can call a kernel or read
/// <c>ThreadIndex.X</c> on the host. They skip only G#'s own output: its managed kernel bodies call the intrinsics, and
/// they run inside a managed launch. Roslyn names a generated tree
/// <c>[&lt;base directory&gt;/]&lt;generator assembly&gt;/&lt;generator type&gt;/&lt;hint name&gt;</c>, so a tree of
/// this assembly sits in a directory named after one of its generator types, inside one named after the assembly.
/// </remarks>
internal static class GSharpGeneratedTrees
{
    private const string AssemblyName = "Kassyi.GSharp.Compiler";

    // The full names of the generator types of this assembly, as Roslyn writes them into the path
    private static readonly string[] s_generatorDirectories =
    [
        AssemblyName + "/" + AssemblyName + "." + nameof(GpuKernelGenerator) + "/",
        AssemblyName + "/" + AssemblyName + "." + nameof(TrustedKeysGenerator) + "/",
        AssemblyName + "/" + AssemblyName + ".NativeApi." + nameof(CudaImportGenerator) + "/",
        AssemblyName + "/" + AssemblyName + ".NativeApi." + nameof(NativeApiGenerator) + "/"
    ];

    /// <summary>Whether a generator of <c>Kassyi.GSharp.Compiler</c> added <paramref name="tree"/>.</summary>
    public static bool IsOwnOutput(SyntaxTree? tree)
    {
        if (tree is null || tree.FilePath.Length == 0)
        {
            return false;
        }

        // The directories end the path right before the hint name, which has no directory of its own
        var path = tree.FilePath.Replace('\\', '/');
        var directory = path.Substring(0, path.LastIndexOf('/') + 1);
        return s_generatorDirectories
            .AsStackEnumerable()
            .Any(directory, static (generator, dir) =>
                dir.EndsWith(generator, StringComparison.Ordinal) &&
                (dir.Length == generator.Length || dir[dir.Length - generator.Length - 1] == '/')
            );
    }
}
