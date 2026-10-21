using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class UnrollHintSadPath
{
    [GpuKernel]
    public static void KernelMethod()
    {
        // Misplaced hint (GSH0017)
        Gpu.Hint.Unroll();
        int a = 0;

        // Invalid factor (GSH0016)
        Gpu.Hint.Unroll(-1);
        for (int i = 0; i < 10; i++)
        {
        }
    }
}
