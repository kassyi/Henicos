// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels for oversized shared memory testing (#799).
/// </summary>
public static partial class OversizedSharedMemoryKernels
{
    [GpuKernel(AllowOversizedSharedMemory = true)]
    public static void FillAndSumOversizedKernel(uint[] output, int n)
    {
        var s = new SharedMemory<uint>(n);
        int tid = ThreadIndex.X;
        for (int i = tid; i < n; i += BlockDimension.X)
        {
            s[i] = (uint)i * 2654435761u + 12345u;
        }

        Gpu.SyncThreads();
        uint sum = 0u;
        for (int j = tid; j < n; j += BlockDimension.X)
        {
            sum += s[n - 1 - j];
        }

        output[tid] = sum;
    }

    [GpuKernel]
    public static void WriteThreadRankOversizedKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        if (tid < output.Length)
        {
            output[tid] = tid + 1;
        }
    }
}
