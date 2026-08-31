using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN_09_AmbiguousDefaultLiteral
{
    [GpuKernel]
    public static void Kernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var x = default; // Ambiguous default literal with 'var'
        output[tid] = 0;
    }
}
