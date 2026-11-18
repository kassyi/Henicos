using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// A span local that the CPU state machine declares again on every resume cannot carry a value from one state into
// another. GSH0039 is reported where the lowered state machine reads it before it writes it in a state, also for the
// splits that the analyzer's flow graph does not have
[SkipSnapshotConsistency("Diagnostics case")]
public static partial class SpanAcrossStatesKernels
{
    // Declared before the loop, read in the body that has the barrier
    [GpuKernel]
    public static void DeclaredBeforeLoop(int[] a, int[] o)
    {
        int tid = ThreadIndex.X;
        int acc = 0;
        Span<int> src = a;
        for (int i = 0; i < 3; i++)
        {
            acc += src[tid];
            Gpu.SyncThreads();
        }

        o[tid] = acc;
    }

    [GpuDeviceFunction]
    public static void StepSpan(Span<int> src, out Span<int> s)
    {
        s = src;
        Gpu.SyncThreads();
    }

    // Written through out by a call that suspends
    [GpuKernel]
    public static void WrittenByOutOfSuspendingCall(int[] a, int[] o)
    {
        int tid = ThreadIndex.X;
        StepSpan(a, out Span<int> s);
        o[tid] = s[tid];
    }

    // Lives only across the reconvergence point that Warp.ActiveMask() adds
    [GpuKernel]
    public static void AcrossReconvergence(int[] data, int[] masks)
    {
        int tid = ThreadIndex.X;
        Span<int> s = data.AsSpan();
        if (tid % 2 == 0)
        {
            Gpu.SyncWarp(0x55555555u);
            return;
        }

        s[tid] = 1;
        masks[tid] = (int)Warp.ActiveMask();
    }

    // Declared in the loop body: every iteration binds it again
    [GpuKernel]
    public static void DeclaredInLoopBody(int[] a, int[] o)
    {
        int tid = ThreadIndex.X;
        int acc = 0;
        for (int i = 0; i < 3; i++)
        {
            Span<int> src = a;
            acc += src[tid];
            Gpu.SyncThreads();
        }

        o[tid] = acc;
    }

    // Created again after the barrier
    [GpuKernel]
    public static void RecreatedAfterBarrier(int[] a, int[] o)
    {
        int tid = ThreadIndex.X;
        Span<int> before = a;
        int x = before[tid];
        Gpu.SyncThreads();
        Span<int> after = a;
        o[tid] = x + after[tid];
    }

    // Written through out by a call that does not suspend, in a method that splits elsewhere
    [GpuDeviceFunction]
    public static void BindSpan(Span<int> src, out Span<int> s) => s = src;

    [GpuKernel]
    public static void WrittenByOutOfPlainCall(int[] a, int[] o)
    {
        int tid = ThreadIndex.X;
        BindSpan(a, out Span<int> s);
        int x = s[tid];
        Gpu.SyncThreads();
        o[tid] = x;
    }

    // Written through out by a call that does not suspend, in the state that a barrier starts
    [GpuKernel]
    public static void WrittenByOutAfterBarrier(int[] a, int[] o)
    {
        int tid = ThreadIndex.X;
        Gpu.SyncThreads();
        BindSpan(a, out Span<int> s);
        o[tid] = s[tid];
    }

    // Used only on the branch without the barrier
    [GpuKernel]
    public static void UsedInOtherBranch(int[] a, int flag)
    {
        Span<int> s = stackalloc int[4];
        if (flag > 0)
        {
            Gpu.SyncThreads();
        }
        else
        {
            s[0] = 1;
            a[0] = s[0];
        }
    }

    // Written through out by a call that does not suspend, in every iteration of a loop that has a barrier
    [GpuKernel]
    public static void WrittenByOutInLoop(int[] a, int[] o)
    {
        int tid = ThreadIndex.X;
        int acc = 0;
        Span<int> s = default;
        for (int i = 0; i < 3; i++)
        {
            BindSpan(a, out s);
            acc += s[tid];
            Gpu.SyncThreads();
        }

        o[tid] = acc;
    }

    [GpuDeviceFunction]
    public static void LateOut(Span<int> src, out Span<int> s)
    {
        Gpu.SyncThreads();
        s = src;
    }

    // The callee writes it after its last barrier: the caller reads it in the call that returns it
    [GpuKernel]
    public static void WrittenAfterTheCalleesBarrier(int[] a, int[] o)
    {
        int tid = ThreadIndex.X;
        LateOut(a, out Span<int> s);
        o[tid] = s[tid];
    }

    [GpuDeviceFunction]
    public static void OutThenBarrierInAnotherCall(Span<int> src, out Span<int> s)
    {
        s = src;
        StepSpan(src, out Span<int> other);
    }

    // The callee writes it and then calls a function that suspends
    [GpuKernel]
    public static void WrittenBeforeANestedSuspendingCall(int[] a, int[] o)
    {
        int tid = ThreadIndex.X;
        OutThenBarrierInAnotherCall(a, out Span<int> s);
        o[tid] = s[tid];
    }

    [GpuDeviceFunction]
    public static void OutBeforeBarrierInOneBranch(Span<int> src, Span<int> other, int flag, out Span<int> s)
    {
        if (flag > 0)
        {
            s = src;
            Gpu.SyncThreads();
        }
        else
        {
            Gpu.SyncThreads();
            s = other;
        }
    }

    // The callee writes it before its barrier on one branch only
    [GpuKernel]
    public static void WrittenBeforeABarrierInOneBranch(int[] a, int[] o, int flag)
    {
        int tid = ThreadIndex.X;
        OutBeforeBarrierInOneBranch(a, a, flag, out Span<int> s);
        o[tid] = s[tid];
    }
}
