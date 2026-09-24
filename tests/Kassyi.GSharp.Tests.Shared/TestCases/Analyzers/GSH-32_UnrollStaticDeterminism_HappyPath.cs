using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public class GSH_32_UnrollStaticDeterminism_HappyPath
{
    [GpuKernel]
    public static unsafe void HappyPath_Unroll(int* data, [Constant] int iterations)
    {
        // 1. Literal constant bound
        Gpu.Hint.Unroll();
        for (int i = 0; i < 10; i++)
        {
            data[i] = 1;
        }

        // 2. const variable bound
        const int N = 20;
        Gpu.Hint.Unroll();
        for (int j = 0; j < N; j++)
        {
            data[j] = 2;
        }

        // 3. [Constant] argument bound
        Gpu.Hint.Unroll();
        for (int k = 0; k < iterations; k++)
        {
            data[k] = 3;
        }
    }
}
