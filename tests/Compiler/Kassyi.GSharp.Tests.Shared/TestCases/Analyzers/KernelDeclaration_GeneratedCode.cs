using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

// One kernel of each kind whose launcher would be a declaration error in the generated C#, and an unrelated direct call
// of a kernel. The generator emits nothing for the rejected kernels, so the C# build reaches the analyzers.
public partial struct GeneratedCodeStruct
{
    [GpuKernel]
    public static void InStruct(int[] o) { o[0] = 42; }
}

public ref partial struct GeneratedCodeRefStruct
{
    [GpuKernel]
    public void Dispose() { }
}

public partial interface IGeneratedCodeStaticAbstract
{
    [GpuKernel]
    static abstract void Abstract(int[] o);
}

public partial interface IGeneratedCodeDefault
{
    [GpuKernel]
    void Default(int[] o) { o[0] = 42; }
}

public sealed class GeneratedCodeBag
{
    public int Count;
}

public ref struct GeneratedCodeScope
{
    public int Value;
}

public static partial class GeneratedCodeKernels
{
    [GpuKernel]
    public static void Generic<T>(T[] o, T value) where T : unmanaged { o[0] = value; }

    [GpuKernel]
    public static GeneratedCodeScope ReturnsRefStruct(int[] o) => default;

    [GpuKernel]
    public static void ClassParameter(GeneratedCodeBag bag, int[] o) { o[0] = 42; }

    [GpuKernel]
    public static void Valid(int[] o) { o[0] = 42; }

    extension(int[] o)
    {
        [GpuKernel]
        public static void InExtensionBlock(int[] p) { p[0] = 42; }
    }
}

public static class GeneratedCodeHost
{
    public static void Run(int[] o) => GeneratedCodeKernels.Valid(o);
}
