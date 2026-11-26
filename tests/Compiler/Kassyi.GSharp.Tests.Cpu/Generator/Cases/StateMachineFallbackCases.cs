using System;
using Kassyi.GSharp.Core;

namespace StateMachineFallbackGeneratorCases;

file struct FileLocalStep
{
    public int Value;
}

public struct FallbackGuard : IDisposable
{
    public int Value;

    public void Dispose() => Value = 0;
}

public ref struct CustomRefBuffer
{
    public int Length;
}

public struct MacroPair
{
    public int Value;

    [GpuOperatorMacro(typeof(FallbackCases), nameof(FallbackCases.AddShuffled))]
    public static MacroPair operator +(MacroPair left, MacroPair right) => throw new GpuOnlyException();
}

[SkipSnapshotConsistency("Input for StateMachineFallbackGeneratorTests")]
public static partial class FallbackCases
{
    [GpuDeviceFunction]
    public static void EligibleSuspendingHelper(int[] data)
    {
        Gpu.SyncThreads();
        data[ThreadIndex.X] += 1;
    }

    [GpuDeviceFunction]
    public static int NonSuspendingHelper(int x) => x + 1;

    [GpuDeviceFunction]
    public static void GridSyncHelper(int[] data)
    {
        Gpu.SyncThreads();
        Gpu.SyncGrid();
        data[ThreadIndex.X] = 1;
    }

    [GpuDeviceFunction]
    public static void DirectlyRecursiveHelper(int depth, int[] data)
    {
        if (depth <= 0)
        {
            return;
        }

        Gpu.SyncThreads();
        DirectlyRecursiveHelper(depth - 1, data);
    }

    [GpuDeviceFunction]
    public static void RecursiveWrapperHelper(int depth, int[] data)
    {
        DirectlyRecursiveHelper(depth, data);
    }

    [GpuDeviceFunction]
    public static uint UsingActiveMaskHelper(int v)
    {
        using var guard = new FallbackGuard { Value = v };
        return Warp.ActiveMask() + (uint)guard.Value;
    }

    [GpuDeviceFunction]
    public static void AddShuffled(ref MacroPair result, MacroPair left, MacroPair right) =>
        result.Value = left.Value + Warp.ShuffleXorSync(0xFFFFFFFFu, right.Value, 1);

    [GpuDeviceFunction]
    private static void FileLocalDeviceHelper(int[] data)
    {
        var step = new FileLocalStep { Value = data[ThreadIndex.X] };
        Gpu.SyncThreads();
        data[ThreadIndex.X] = step.Value + 1;
    }

    [GpuDeviceFunction]
    private static void MacroOperatorDeviceHelper(int[] data)
    {
        Gpu.SyncThreads();
        var pair = new MacroPair { Value = data[ThreadIndex.X] };
        pair = pair + pair;
        data[ThreadIndex.X] = pair.Value;
    }

    [GpuKernel]
    public static void EligibleBarrierKernel(int[] data)
    {
        EligibleSuspendingHelper(data);
    }

    [GpuKernel]
    public static void NoBarrierKernel(int[] data)
    {
        data[ThreadIndex.X] = NonSuspendingHelper(data[ThreadIndex.X]);
    }

    [GpuKernel]
    public static void GridSyncKernel(int[] data)
    {
        GridSyncHelper(data);
    }

    [GpuKernel(Cooperative = true)]
    public static void CooperativeKernel(int[] data)
    {
        Gpu.SyncThreads();
        data[ThreadIndex.X] = 1;
    }

    [GpuKernel(Cooperative = true)]
    public static void CooperativeAndUsingKernel(int[] data)
    {
        using var guard = new FallbackGuard { Value = ThreadIndex.X };
        data[ThreadIndex.X] = (int)Warp.ActiveMask() + guard.Value;
    }

    [GpuKernel]
    public static void RecursiveCallKernel(int depth, int[] data)
    {
        RecursiveWrapperHelper(depth, data);
    }

    [GpuKernel]
    public static void ShuffleCalleeKernel(int[] data)
    {
        data[ThreadIndex.X] = Warp.ShuffleXorSync(0xFFFFFFFFu, ThreadIndex.X, 1);
    }

    [GpuKernel]
    public static void ShuffleCallingKernelKernel(int[] data)
    {
        ShuffleCalleeKernel(data);
        data[0] = Warp.ShuffleXorSync(0xFFFFFFFFu, data[0], 1);
    }

    [GpuKernel]
    public static void FloatSwitchNaNKernel(int[] data, float selector)
    {
        int value = ThreadIndex.X;
        switch (selector)
        {
            case float.NaN:
                value = Warp.ShuffleXorSync(0xFFFFFFFFu, value, 1);
                break;
            default:
                value += 1;
                break;
        }

        data[ThreadIndex.X] = value;
    }

    [GpuKernel]
    public static void WarpOperatorMacroKernel(int[] data)
    {
        var pair = new MacroPair { Value = data[ThreadIndex.X] };
        Gpu.SyncThreads();
        pair = pair + pair;
        data[ThreadIndex.X] = pair.Value;
    }

    [GpuKernel]
    public static void UsingActiveMaskKernel(int[] data)
    {
        using var guard = new FallbackGuard { Value = ThreadIndex.X };
        data[ThreadIndex.X] = (int)Warp.ActiveMask() + guard.Value;
    }

    [GpuDeviceFunction]
    private static bool TryReadMask(int input, out int value)
    {
        value = input + (int)Warp.ActiveMask();
        return value != 0;
    }

    [GpuKernel]
    public static void StackBodyLoweringKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        if (tid < 0)
        {
            int value = 0;
            data[tid] = value;
        }

        if (data[tid] > 0 && TryReadMask(data[tid], out int value))
        {
            data[tid] = value;
        }
    }

    [GpuKernel]
    public static void NonSpanRefStructKernel(int[] data, CustomRefBuffer buffer)
    {
        Gpu.SyncThreads();
        data[ThreadIndex.X] = buffer.Length;
    }

    [GpuKernel]
    public static void CallsFileLocalDeviceKernel(int[] data)
    {
        FileLocalDeviceHelper(data);
    }

    [GpuKernel]
    public static void FileLocalUnavailableKernel(int[] data)
    {
        var step = new FileLocalStep { Value = data[ThreadIndex.X] };
        Gpu.SyncThreads();
        data[ThreadIndex.X] = step.Value;
    }

    [GpuKernel]
    public static void CallsMacroOperatorDeviceKernel(int[] data)
    {
        MacroOperatorDeviceHelper(data);
    }

    [GpuKernel]
    public static void CallsInstanceDeviceKernel(int[] data)
    {
        var holder = new InstanceDeviceHolder();
        holder.InstanceSuspendingHelper(data);
    }

    [GpuKernel]
    public static void CallsNonPartialDeviceKernel(int[] data)
    {
        NonPartialHolder.NonPartialSuspendingHelper(data);
    }
}

public partial struct InstanceDeviceHolder
{
    public int Offset;

    [GpuDeviceFunction]
    public void InstanceSuspendingHelper(int[] data)
    {
        Gpu.SyncThreads();
        data[ThreadIndex.X] = Offset;
    }
}

public static class NonPartialHolder
{
    [GpuDeviceFunction]
    public static void NonPartialSuspendingHelper(int[] data)
    {
        Gpu.SyncThreads();
        data[ThreadIndex.X] += 1;
    }
}
