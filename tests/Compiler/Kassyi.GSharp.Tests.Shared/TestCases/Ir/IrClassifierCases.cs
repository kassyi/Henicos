using System;
using Kassyi.GSharp.Core;

namespace IrClassifierCases;

public struct Counter
{
    public int Count;

    // A getter that changes its receiver
    public int NextInt
    {
        get
        {
            Count += 1;
            return Count;
        }
    }

    // Count is read, then the getter of NextInt changes it through the implicit this
    public int ReadThenNext => Count + NextInt;
}

// Each case method holds 'var result = <expression>;' or the statement the test looks at
public static class Cases
{
    [GpuDeviceFunction]
    public static int Pure(int x) => x + 1;

    [GpuDeviceFunction]
    public static void SetRef(ref int x) => x = 1;

    [GpuDeviceFunction]
    public static void WriteArray(int[] data) => data[0] = 1;

    [GpuDeviceFunction]
    public static int SuspendingValue(int x)
    {
        Gpu.SyncThreads();
        return x;
    }

    [GpuDeviceFunction]
    public static void SuspendingVoid() => Gpu.SyncThreads();

    [GpuKernel]
    public static void BlockBarrier() => Gpu.SyncThreads();

    [GpuKernel]
    public static void GridBarrier() => Gpu.SyncGrid();

    [GpuKernel]
    public static void SyncWarp() => Gpu.SyncWarp(0xffffffffu);

    [GpuKernel]
    public static void ShuffleDown(int x)
    {
        var result = Warp.ShuffleDownSync(0xffffffffu, x, 1);
    }

    [GpuKernel]
    public static void ActiveMask()
    {
        var result = Warp.ActiveMask();
    }

    [GpuKernel]
    public static void Fence() => Gpu.ThreadFence();

    [GpuKernel]
    public static void CallsSuspendingValue(int x)
    {
        var result = SuspendingValue(x);
    }

    [GpuKernel]
    public static void CallsSuspendingVoid() => SuspendingVoid();

    [GpuKernel]
    public static void CallsPure(int x)
    {
        var result = Pure(x);
    }

    [GpuKernel]
    public static void CallsSetRef(int x) => SetRef(ref x);

    [GpuKernel]
    public static void CallsWriteArray(int[] data) => WriteArray(data);

    [GpuKernel]
    public static void ScalarMath(int a, int b)
    {
        var result = Math.Max(a, b);
    }

    [GpuKernel]
    public static void NestedSplit(int x)
    {
        var result = Pure(SuspendingValue(x)) + Warp.ShuffleXorSync(0xffffffffu, x, 2);
    }

    [GpuKernel]
    public static void NoCall(int a, int b)
    {
        var result = a + b;
    }

    [GpuKernel]
    public static void LocalPlusSetRef(int x)
    {
        var result = x + Read(ref x);
    }

    [GpuKernel]
    public static void ElementPlusWriteArray(int[] data)
    {
        var result = data[0] + Write(data);
    }

    [GpuKernel]
    public static void DiscardOut()
    {
        var result = Out(out _);
    }

    [GpuKernel]
    public static void AsSpanView(int[] data)
    {
        var result = data.AsSpan();
    }

    [GpuKernel]
    public static void BarrierInLoop(int[] data)
    {
        for (var i = 0; i < 2; i++)
        {
            Gpu.SyncThreads();
        }

        data[0] = 1;
    }

    [GpuKernel]
    public static void FenceInLoop(int[] data)
    {
        for (var i = 0; i < 2; i++)
        {
            Gpu.ThreadFence();
        }

        data[0] = 1;
    }

    [GpuDeviceFunction]
    public static int Read(ref int x) => x++;

    [GpuDeviceFunction]
    public static int Write(int[] data) => data[0]++;

    [GpuDeviceFunction]
    public static int Out(out int x)
    {
        x = 1;
        return 0;
    }
}
