using Kassyi.GSharp.Core;

namespace ManagedStackOperatorMacroCases;

public struct Value
{
    public int V;

    [GpuOperatorMacro(typeof(MacroKernels), nameof(MacroKernels.Add))]
    public static Value operator +(Value left, Value right) => throw new GpuOnlyException();
}

// Kernels that the stack model takes but whose operator + calls a device function that reaches a warp collective: the
// stack form of the target takes a frame offset that the operator cannot pass, so the kernels keep their state
// machines
[SkipSnapshotConsistency("Input for ManagedStackModelGeneratorTests")]
public static partial class MacroKernels
{
    [GpuKernel]
    public static void MacroAfterBarrierKernel(int[] data)
    {
        int t = ThreadIndex.X;
        Value x;
        x.V = data[t];
        Gpu.SyncThreads();
        x = x + x;
        data[t] = x.V;
    }

    [GpuKernel]
    public static void MacroAndShuffleKernel(int[] data)
    {
        int t = ThreadIndex.X;
        Value x;
        x.V = Warp.ShuffleXorSync(0xFFFFFFFFu, data[t], 2);
        x = x + x;
        data[t] = x.V;
    }

    [GpuDeviceFunction]
    public static void Add(ref Value result, Value left, Value right) =>
        result.V = left.V + Warp.ShuffleXorSync(0xFFFFFFFFu, right.V, 1);
}
