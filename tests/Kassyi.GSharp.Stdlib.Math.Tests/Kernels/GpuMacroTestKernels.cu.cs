using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Stdlib.Math.Tests.Kernels;

public static class GpuMacroTestKernels
{
    [GpuKernel]
    public static void Kernel_MacroTest_FillAndSingle(uint[] outputBuffer, int length, uint fillValue, uint singleValue)
    {
        // GridStrideLoop to fill the array with fillValue
        Gpu.GridStrideLoop(length, i =>
        {
            outputBuffer[i] = fillValue;
        });

        // Ensure all threads within block have finished writing
        Gpu.SyncThreads();

        // SingleThread to overwrite the first element
        Gpu.SingleThread(() =>
        {
            if (length > 0)
            {
                outputBuffer[0] = singleValue;
            }
        });
    }
}
