using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Transpiler.Emission;

public struct SpillOrderMutatingCounter
{
    public int Value;

    public int Next => ++Value;

    public int this[int delta]
    {
        get
        {
            Value += delta;
            return Value;
        }
    }
}

/// <summary>
/// Managed execution test cases for #992 (non-readonly struct property getter / indexer mutating value parameters and
/// receivers across barriers and arguments).
/// </summary>
[SkipSnapshotConsistency("Managed execution test cases for #992 shapes")]
public static partial class SpillOrderMutatingGetterShapeKernels
{
    [GpuDeviceFunction]
    private static int SuspendingCombineCounter(SpillOrderMutatingCounter snapshot, int afterMutation)
    {
        Gpu.SyncThreads();
        return snapshot.Value * 100 + afterMutation;
    }

    [GpuDeviceFunction]
    private static int ReadAndMutateAcrossBarrier(SpillOrderMutatingCounter counter, int bonus)
    {
        int before = counter.Next + bonus;
        Gpu.SyncThreads();
        return before * 100 + counter.Value;
    }

    // Shape 1 (#992): Kernel value parameter mutated via non-readonly property getter before Gpu.SyncThreads()
    [GpuKernel]
    public static void MutatingGetterOnKernelValueParameterAcrossBarrierKernel(
        int[] output,
        SpillOrderMutatingCounter counter)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int first = counter.Next + tid;
        Gpu.SyncThreads();
        output[tid] = first * 100 + counter.Value;
    }

    public static void RunMutatingGetterOnKernelValueParameterAcrossBarrier(int[] output, int initialValue, uint blockDimX) =>
        LaunchMutatingGetterOnKernelValueParameterAcrossBarrierKernel(
            output,
            new SpillOrderMutatingCounter { Value = initialValue },
            blockDimX: blockDimX);

    // Shape 2 (#992): Suspending device function value parameter mutated via non-readonly property getter before Gpu.SyncThreads()
    [GpuKernel]
    public static void MutatingGetterOnDeviceValueParameterAcrossBarrierKernel(
        int[] output,
        SpillOrderMutatingCounter counter)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        output[tid] = ReadAndMutateAcrossBarrier(counter, tid);
    }

    public static void RunMutatingGetterOnDeviceValueParameterAcrossBarrier(int[] output, int initialValue, uint blockDimX) =>
        LaunchMutatingGetterOnDeviceValueParameterAcrossBarrierKernel(
            output,
            new SpillOrderMutatingCounter { Value = initialValue },
            blockDimX: blockDimX);

    // Shape 3 (#992): Kernel value parameter mutated via non-readonly indexer getter before Gpu.SyncThreads()
    [GpuKernel]
    public static void MutatingIndexerOnKernelValueParameterAcrossBarrierKernel(
        int[] output,
        SpillOrderMutatingCounter counter)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int step = tid + 2;
        int first = counter[step];
        Gpu.SyncThreads();
        output[tid] = first * 100 + counter.Value;
    }

    public static void RunMutatingIndexerOnKernelValueParameterAcrossBarrier(int[] output, int initialValue, uint blockDimX) =>
        LaunchMutatingIndexerOnKernelValueParameterAcrossBarrierKernel(
            output,
            new SpillOrderMutatingCounter { Value = initialValue },
            blockDimX: blockDimX);

    // Shape 4 (#992 / F10): Suspending call argument order where arg0 is struct identifier `counter` and arg1 invokes mutating getter `counter.Next`
    [GpuKernel]
    public static void MutatingGetterAfterStructIdentifierArgumentKernel(int[] output, int seed)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        var counter = new SpillOrderMutatingCounter { Value = seed + tid };
        int combined = SuspendingCombineCounter(counter, counter.Next);
        output[tid] = combined * 100 + counter.Value;
    }

    public static void RunMutatingGetterAfterStructIdentifierArgument(int[] output, int seed, uint blockDimX) =>
        LaunchMutatingGetterAfterStructIdentifierArgumentKernel(output, seed, blockDimX: blockDimX);

    // Shape 5 (#992 / F10): Suspending call argument order where arg0 is struct identifier `counter` and arg1 invokes mutating indexer `counter[3]`
    [GpuKernel]
    public static void MutatingIndexerAfterStructIdentifierArgumentKernel(int[] output, int seed)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        var counter = new SpillOrderMutatingCounter { Value = seed + tid };
        int combined = SuspendingCombineCounter(counter, counter[3]);
        output[tid] = combined * 100 + counter.Value;
    }

    public static void RunMutatingIndexerAfterStructIdentifierArgument(int[] output, int seed, uint blockDimX) =>
        LaunchMutatingIndexerAfterStructIdentifierArgumentKernel(output, seed, blockDimX: blockDimX);
}
