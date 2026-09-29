using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public class GSH_32_33_UnrollStaticDeterminism
{
    [GpuKernel]
    public static unsafe void SadPath_GSH0032_DynamicBounds(int* data, int dynamicLength, [GpuReadOnly] int gpuConstLength)
    {
        // Error GSH0032: Normal dynamic parameter
        Gpu.Hint.Unroll();
        for (int i = 0; i < dynamicLength; i++)
        {
            data[i] = 1;
        }

        // Error GSH0032: [GpuReadOnly] is an execution-time constant, not compile-time
        Gpu.Hint.Unroll();
        for (int j = 0; j < gpuConstLength; j++)
        {
            data[j] = 2;
        }

        int localDynamic = data[0];
        // Error GSH0032: Local variable assigned dynamically
        Gpu.Hint.Unroll();
        for (int k = 0; k < localDynamic; k++)
        {
            data[k] = 3;
        }
    }

    [GpuKernel]
    public static unsafe void SadPath_GSH0033_DynamicControlFlow(int* data)
    {
        // Warning GSH0033: While loop with break
        int i = 0;
        Gpu.Hint.Unroll();
        while (i < 10)
        {
            if (data[i] == 0)
            {
                break;
            }

            i++;
        }

        // Warning GSH0033: For loop with return
        Gpu.Hint.Unroll();
        for (int j = 0; j < 10; j++)
        {
            if (data[j] == 1)
            {
                return;
            }
        }
    }
}
