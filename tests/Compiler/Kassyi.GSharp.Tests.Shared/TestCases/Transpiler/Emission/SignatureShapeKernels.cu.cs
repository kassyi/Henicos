using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

[SkipSnapshotConsistency("Emission shapes checked line by line by EmissionShapeTests")]
public static partial class SignatureShapeKernels
{
    // Value-returning kernels: without parameters, with parameters, and with a state machine context (SyncGrid)
    [GpuKernel]
    public static int ExpressionResultKernel() => 7;

    [GpuKernel]
    public static int BlockResultKernel(int[] input)
    {
        return input[ThreadIndex.X] + 1;
    }

    [GpuKernel]
    public static int GridSyncResultKernel()
    {
        int value = ThreadIndex.X;
        Gpu.SyncGrid();
        return value;
    }

    // Inlining keywords from [MethodImpl]: no argument, a short argument, and an enum argument
    [GpuDeviceFunction]
    [MethodImpl]
    public static int DefaultImpl(int x) => x + 1;

    [GpuDeviceFunction]
    [MethodImpl((short)MethodImplOptions.NoInlining)]
    public static int ShortNoInlining(int x) => x + 2;

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int EnumAggressiveInlining(int x) => x + 3;

    [GpuKernel]
    public static void InliningKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = DefaultImpl(tid) + ShortNoInlining(tid) + EnumAggressiveInlining(tid);
    }

    // Suspending device functions (a barrier inside): in and ref readonly parameters, and no parameter at all
    [GpuDeviceFunction]
    public static int SuspendingReadOnlyParameters(in int a, ref readonly int b)
    {
        int sum = a + b;
        Gpu.SyncThreads();
        return sum;
    }

    [GpuDeviceFunction]
    public static int SuspendingWithoutParameters()
    {
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        return tid;
    }

    [GpuKernel]
    public static void SuspendingCallKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int a = tid;
        int b = tid * 2;
        output[tid] = SuspendingReadOnlyParameters(in a, in b) + SuspendingWithoutParameters();
    }
}
