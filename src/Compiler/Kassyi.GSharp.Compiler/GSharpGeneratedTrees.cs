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
    private const string GpuKernelGeneratorName = AssemblyName + "." + nameof(GpuKernelGenerator);
    private const string TrustedKeysGeneratorName = AssemblyName + "." + nameof(TrustedKeysGenerator);
    private const string CudaImportGeneratorName = AssemblyName + ".NativeApi." + nameof(CudaImportGenerator);
    private const string NativeApiGeneratorName = AssemblyName + ".NativeApi." + nameof(NativeApiGenerator);

    /// <summary>Whether a generator of <c>Kassyi.GSharp.Compiler</c> added <paramref name="tree"/>.</summary>
    public static bool IsOwnOutput(SyntaxTree? tree)
    {
        if (tree is null)
        {
            return false;
        }

        // <assembly>/<generator type>/<hint name>, at the end of the path; the hint name has no directory of its own
        var path = tree.FilePath.AsSpan();
        if (!TrySplitLast(path, out var directory, out _) ||
            !TrySplitLast(directory, out var parent, out var generatorType))
        {
            return false;
        }

        var assembly = TrySplitLast(parent, out _, out var last) ? last : parent;
        return assembly is AssemblyName &&
               (generatorType is GpuKernelGeneratorName or TrustedKeysGeneratorName or CudaImportGeneratorName
                   or NativeApiGeneratorName);
    }

    // The path before its last separator ('/' or '\') and the segment after it, or false when it has none
    private static bool TrySplitLast(ReadOnlySpan<char> path, out ReadOnlySpan<char> before,
        out ReadOnlySpan<char> segment)
    {
        var separator = path.LastIndexOfAny('/', '\\');
        before = separator < 0 ? ReadOnlySpan<char>.Empty : path.Slice(0, separator);
        segment = separator < 0 ? ReadOnlySpan<char>.Empty : path.Slice(separator + 1);
        return separator >= 0;
    }
}
