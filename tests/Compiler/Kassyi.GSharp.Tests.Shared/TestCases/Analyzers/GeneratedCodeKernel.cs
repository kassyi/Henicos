using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

// GSH0031 in generated code: the kernel that GeneratedCodeCaller.cs calls from a tree at a generated path
public static partial class GeneratedCodeKernels
{
    [GpuKernel]
    public static void K(int[] o)
    {
        o[0] = 42;
    }
}
