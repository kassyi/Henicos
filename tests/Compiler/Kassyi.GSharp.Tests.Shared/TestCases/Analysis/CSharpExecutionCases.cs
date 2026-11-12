using System;
using System.Diagnostics;
using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace CSharpExecutionCases;

public struct Value
{
    public int V;

    [GpuOperatorMacro(typeof(Kernels), nameof(Kernels.Add))]
    public static Value operator +(Value left, Value right) => throw new GpuOnlyException();
}

// The inputs of CSharpExecutionAnalysisTests and CSharpExecutionPlanTests
public static partial class Kernels
{
    [GpuKernel]
    public static void Plain(int[] data) => data[ThreadIndex.X] += 1;

    [GpuKernel]
    public static void CallsPlainFunction(int[] data) => data[ThreadIndex.X] = Twice(data[ThreadIndex.X]);

    [GpuKernel]
    public static void IntSum(int[] data, int[] result) => result[0] = data.AsStackEnumerable().Sum();

    // C# removes the call, so the barrier never runs
    [GpuKernel]
    public static void ConditionalBarrier(int[] data)
    {
        Removed();
        data[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    public static void BlockBarrier(int[] data)
    {
        data[ThreadIndex.X] = 1;
        Gpu.SyncThreads();
    }

    [GpuKernel(Cooperative = true)]
    public static void GridBarrier(int[] data)
    {
        data[ThreadIndex.X] = 1;
        Gpu.SyncGrid();
    }

    [GpuKernel]
    public static void Shuffle(int[] data) =>
        data[ThreadIndex.X] = Warp.ShuffleDownSync(0xFFFFFFFFu, data[ThreadIndex.X], 1u);

    [GpuKernel]
    public static void ActiveMask(uint[] data) => data[ThreadIndex.X] = Warp.ActiveMask();

    [GpuKernel]
    public static void AsyncBarrier(ulong[] data) => Gpu.MBarrierInit(ref data[0], 32u);

    [GpuKernel]
    public static void SharedTile(int[] data)
    {
        var tile = new SharedMemory<int>(32);
        tile[ThreadIndex.X] = data[ThreadIndex.X];
        data[ThreadIndex.X] = tile[ThreadIndex.X];
    }

    [GpuKernel]
    public static void BarrierThroughFunction(int[] data) => data[ThreadIndex.X] = Exchange(data);

    [GpuKernel]
    public static void BarrierThroughTwoFunctions(int[] data) => data[ThreadIndex.X] = CallsExchange(data);

    [GpuKernel]
    public static void BarrierInLambda(int[] data) => Gpu.ForUnroll(0, 2, i =>
    {
        data[i] = 1;
        Gpu.SyncThreads();
    });

    [GpuKernel]
    public static void MethodGroupOfBarrierFunction(int[] data) => Gpu.ForUnroll(0, 2, StepWithBarrier);

    [GpuKernel]
    public static void OperatorMacro(int[] data)
    {
        Value x = new Value { V = data[0] };
        Value z = new Value();
        z = x + x;
        data[ThreadIndex.X] = z.V;
    }

    [GpuKernel]
    public static void FloatSum(float[] data, float[] result) => result[0] = data.AsStackEnumerable().Sum();

    [GpuKernel]
    public static void InlinePtx(int[] data)
    {
        Gpu.InlinePtx("nop;");
        data[0] = 1;
    }

    [GpuKernel]
    public static void Cycle(int[] data) => data[0] = Ping(data, 3);

    [GpuKernel]
    public static int Parameters(int a, in int b, ref int c, out int d, Span<int> e, ReadOnlySpan<int> f, float[]? g)
    {
        d = a + b + c;
        return e.Length + f.Length + (g?.Length ?? 0);
    }

    [GpuDeviceFunction]
    public static void Add(ref Value result, Value left, Value right) => result.V = left.V + right.V;

    [GpuDeviceFunction]
    private static int Twice(int x) => x * 2;

    [Conditional("NOT_DEFINED_SYMBOL")]
    [GpuDeviceFunction]
    private static void Removed() => Gpu.SyncThreads();

    [GpuDeviceFunction]
    private static int Exchange(int[] data)
    {
        Gpu.SyncThreads();
        return data[(ThreadIndex.X + 1) % BlockDimension.X];
    }

    [GpuDeviceFunction]
    private static int CallsExchange(int[] data) => Exchange(data) + 1;

    [GpuDeviceFunction]
    private static void StepWithBarrier(int i) => Gpu.SyncThreads();

    [GpuDeviceFunction]
    private static int Ping(int[] data, int n) => n == 0 ? 0 : Pong(data, n - 1);

    [GpuDeviceFunction]
    private static int Pong(int[] data, int n) => n == 0 ? data[0] : Ping(data, n - 1);
}
