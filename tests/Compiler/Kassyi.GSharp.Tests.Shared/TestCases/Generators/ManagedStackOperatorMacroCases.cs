using Kassyi.GSharp.Core;

namespace ManagedStackOperatorMacroCases;

public struct Value
{
    public int V;

    [GpuOperatorMacro(typeof(MacroKernels), nameof(MacroKernels.Add))]
    public static Value operator +(Value left, Value right) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(MacroKernels), nameof(MacroKernels.SubActive))]
    public static Value operator -(Value left, Value right) => throw new GpuOnlyException();
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

    // A kernel that reaches Warp.ActiveMask() writes the body lowered for the stacks: the operator there reaches
    // Warp.ActiveMask() through its target as well, and the kernel keeps its state machine the same way
    [GpuKernel]
    public static void MacroAndActiveMaskKernel(int[] data)
    {
        int t = ThreadIndex.X;
        Value x;
        x.V = data[t];
        if (t % 2 == 0)
        {
            x.V += (int)Warp.ActiveMask();
        }

        x = x - x;
        data[t] = x.V;
    }

    [GpuDeviceFunction]
    public static void Add(ref Value result, Value left, Value right) =>
        result.V = left.V + Warp.ShuffleXorSync(0xFFFFFFFFu, right.V, 1);

    [GpuDeviceFunction]
    public static void SubActive(ref Value result, Value left, Value right) =>
        result.V = left.V - right.V + (int)Warp.ActiveMask();
}
