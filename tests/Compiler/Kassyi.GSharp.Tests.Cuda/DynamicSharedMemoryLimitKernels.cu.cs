using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// Kernels that use n uints of dynamic shared memory (n * 4 bytes of the launch's sharedMemBytes) for the CUDA launch
/// limit tests (#545). Each uses the whole region: every element is written by one thread and read by another, so a
/// region shorter than requested changes the sums.
/// </summary>
[SkipSnapshotConsistency]
public static partial class DynamicSharedMemoryLimitKernels
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

    // The same as FillAndSumKernel, used only by the test that checks a launch within the default limit never opts in
    [GpuKernel]
    public static void DefaultLimitKernel(uint[] output, int n)
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

    // The same as FillAndSumKernel, used only by the test that compares the occupancy before and after the opt-in
    [GpuKernel]
    public static void OccupancyKernel(uint[] output, int n)
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

    // 4 KB of static shared memory (1024 uints) and n uints of dynamic shared memory; output[t] adds the
    // thread's slice of both, each written by another thread
    [GpuKernel]
    public static void StaticPlusDynamicKernel(uint[] output, int n)
    {
        var fixedPart = new SharedMemory<uint>(1024);
        var s = new SharedMemory<uint>(n);
        int tid = ThreadIndex.X;
        for (int i = tid; i < 1024; i += BlockDimension.X)
        {
            fixedPart[i] = (uint)i * 2654435761u + 12345u;
        }

        for (int i = tid; i < n; i += BlockDimension.X)
        {
            s[i] = (uint)i * 2654435761u + 12345u;
        }

        Gpu.SyncThreads();
        uint sum = 0u;
        for (int j = tid; j < 1024; j += BlockDimension.X)
        {
            sum += fixedPart[1023 - j];
        }

        for (int j = tid; j < n; j += BlockDimension.X)
        {
            sum += s[n - 1 - j];
        }

        output[tid] = sum;
    }
}
