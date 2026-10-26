using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// GSH0077: on CUDA each thread places the dynamic SharedMemory<T> regions of its block in the order it first reaches
// them, so a declaration that only some threads reach, or a count that differs between threads, can make the threads
// disagree on where a region is (#439)
public static partial class DivergentDynamicSharedMemoryKernels
{
    [GpuDeviceFunction]
    public static int Scratch(int n)
    {
        var scratch = new SharedMemory<int>(n);
        scratch[ThreadIndex.X] = n;
        return scratch[ThreadIndex.X];
    }

    // Reaches the declaration of Scratch through a call
    [GpuDeviceFunction]
    public static int ScratchThrough(int n) => Scratch(n) + 1;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void DeclarationInBranchKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        if (tid < 16)
        {
            var half = new SharedMemory<int>(n);
            half[tid] = tid;
            output[tid] = half[tid];
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CallInBranchKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        int r = tid % 2 == 0 ? Scratch(n) : 0;
        if (tid < 16)
        {
            r += ScratchThrough(n);
        }

        output[tid] = r;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ThreadCountKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        var perThread = new SharedMemory<int>(n + tid);
        perThread[0] = tid;
        output[tid] = perThread[0];
    }

    // In a device function, under a loop whose condition depends on the lane
    [GpuDeviceFunction]
    public static int LaneLoop(int n)
    {
        int sum = 0;
        for (int i = 0; i < Gpu.LaneId(); i++)
        {
            var row = new SharedMemory<int>(n);
            sum += row[i];
        }

        return sum;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void LaneLoopKernel(int[] output, int n)
    {
        output[ThreadIndex.X] = LaneLoop(n);
    }

    // Two thread-variant conditions enclose the declaration; it is reported once
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NestedBranchKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        if (tid < 16)
        {
            while (tid < 8)
            {
                var nested = new SharedMemory<int>(n);
                nested[tid] = tid;
                output[tid] = nested[tid];
                break;
            }
        }
    }

    // An arm of a switch expression on a thread-variant value
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void SwitchArmKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        int r = tid switch
        {
            0 => Scratch(n),
            _ => 0
        };
        output[tid] = r;
    }
}
