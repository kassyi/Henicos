// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels for <see cref="TargetSharedMemoryDifferentialTests"/>: dynamic shared memory at and above the target GPU's
/// per-block limit (#799).
/// </summary>
public static partial class TargetSharedMemoryDifferentialKernels
{
    // s[i] = i * 2654435761 + 12345 (uint arithmetic); output[t] = sum of s[n - 1 - j] for j = t, t + blockDim, ...
    [GpuKernel]
    public static void FillAndSumKernel(uint[] output, int n)
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

    // Uses neither shared memory nor barriers, so the C# execution path runs it on both dialects
    [GpuKernel]
    public static void WriteThreadRankKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        if (tid < output.Length)
        {
            output[tid] = tid + 1;
        }
    }
}
