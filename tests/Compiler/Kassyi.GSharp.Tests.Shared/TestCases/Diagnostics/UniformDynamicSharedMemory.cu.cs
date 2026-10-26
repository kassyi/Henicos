using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0077 boundary: every thread of the block reaches these dynamic SharedMemory<T> declarations with the same count,
// and the fixed-size declaration and the call in thread-variant branches do not use the dynamic buffer
public static partial class UniformDynamicSharedMemoryKernels
{
    [GpuDeviceFunction]
    public static int Scratch(int n)
    {
        var scratch = new SharedMemory<int>(n);
        scratch[ThreadIndex.X] = n;
        return scratch[ThreadIndex.X];
    }

    [GpuDeviceFunction]
    public static int Twice(int value) => value * 2;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void UniformConditionKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        int r = 0;
        if (n > 0)
        {
            var s = new SharedMemory<int>(n);
            s[tid] = tid;
            r = s[tid] + Scratch(n);
        }

        var perBlock = new SharedMemory<int>(BlockDimension.X);
        perBlock[tid] = r;
        output[tid] = perBlock[tid];
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void FixedSizeInBranchKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        int r = Scratch(n);
        if (tid < 16)
        {
            var fixedSize = new SharedMemory<int>(32);
            fixedSize[tid] = Twice(tid);
            r += fixedSize[tid];
        }

        output[tid] = r;
    }
}
