using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers.BarrierUsage;

// GSH0039 / GSH0041 follow the flow of control: a local is reported when it is used on some path after a point where
// the CPU dialects split the state machine, not when a use only comes later in the text (A-21, #668)
[SkipSnapshotConsistency("Analyzer case")]
public static partial class CrossBarrierFlowKernels
{
    // No diagnostic: the branch with the barrier never reaches the use
    [GpuKernel]
    public static void ExclusiveBranchKernel(int[] buffer, int flag)
    {
        Span<int> span = buffer;
        if (flag > 0)
        {
            Gpu.SyncThreads();
        }
        else
        {
            span[0] = 1;
        }
    }

    // GSH0039: the next iteration uses the span after the barrier
    [GpuKernel]
    public static void BackEdgeKernel(int[] buffer, int count)
    {
        Span<int> span = buffer;
        for (int k = 0; k < count; k++)
        {
            span[0] = k;
            Gpu.SyncThreads();
        }
    }

    // No diagnostic: every iteration declares the span again before it uses it
    [GpuKernel]
    public static void DeclaredInLoopKernel(int[] buffer, int count)
    {
        for (int k = 0; k < count; k++)
        {
            Span<int> span = buffer;
            span[0] = k;
            Gpu.SyncThreads();
        }
    }

    // GSH0041: the next iteration writes through the stackalloc pointer after the barrier
    [GpuKernel]
    public static unsafe void PointerBackEdgeKernel(int[] buffer, int count)
    {
        int* scratch = stackalloc int[4];
        for (int k = 0; k < count; k++)
        {
            scratch[0] = k;
            Gpu.SyncThreads();
        }
    }

    // No diagnostic: the barrier and the use are in different sections of the switch
    [GpuKernel]
    public static void SwitchSectionKernel(int[] buffer, int mode)
    {
        Span<int> span = buffer;
        switch (mode)
        {
            case 0:
                Gpu.SyncThreads();
                break;
            case 1:
                span[0] = 1;
                break;
        }
    }

    // GSH0039: a warp shuffle splits the state machine as a barrier does
    [GpuKernel]
    public static void ShuffleKernel(int[] buffer)
    {
        Span<int> span = buffer;
        int value = Warp.ShuffleDownSync(0xFFFFFFFFu, span[ThreadIndex.X], 1u);
        span[ThreadIndex.X] = value;
    }

    // GSH0039: an explicit grid sync splits the state machine (#668)
    [GpuKernel(Cooperative = true)]
    public static void ExplicitGridSyncKernel(int[] a)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Span<int> s = stackalloc int[4];
        s[0] = tid;
        Gpu.SyncGrid();
        a[tid] = s[0];
    }

    // GSH0039: the gap between two statements of a Gpu.SyncGrid(body) splits the state machine (#668)
    [GpuKernel(Cooperative = true)]
    public static void SyncGridBodyGapKernel(int[] a)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            Span<int> s = stackalloc int[4];
            s[0] = tid;
            a[tid] = s[0];
        });
    }

    // No diagnostic: the span is used only within one statement of the body, between the gaps
    [GpuKernel(Cooperative = true)]
    public static void SyncGridBodySingleStatementKernel(int[] a)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        Gpu.SyncGrid(() =>
        {
            a[tid] = 1;
            {
                Span<int> s = stackalloc int[4];
                s[0] = tid;
                a[tid] += s[0];
            }
        });
    }

    // GSH0041: the next iteration of the unrolled loop writes through the pointer after the barrier in the body
    [GpuKernel]
    public static unsafe void ForUnrollBodyKernel(int[] buffer)
    {
        int* scratch = stackalloc int[4];
        Gpu.ForUnroll(0, 2, i =>
        {
            scratch[i] = i;
            Gpu.SyncThreads();
        });
    }
    // V, GSH0039: a write through the ref local after the barrier uses the reference
    [GpuKernel]
    public static void RefWriteAfterBarrierKernel(int[] buffer)
    {
        ref int first = ref buffer[0];
        Gpu.SyncThreads();
        first = 20;
    }

    // N, GSH0041: the lambda run after the barrier reads the captured pointer
    [GpuKernel]
    public static unsafe void PointerCapturedByLambdaKernel(int[] a)
    {
        int* p = stackalloc int[4];
        p[0] = 1;
        Gpu.SyncThreads();
        Gpu.SingleThread(() => { a[0] = p[0]; });
    }

    // N2, GSH0041: the local function called after the barrier reads the captured pointer
    [GpuKernel]
    public static unsafe void PointerCapturedByLocalFunctionKernel(int[] a)
    {
        int* p = stackalloc int[4];
        p[0] = 1;
        Gpu.SyncThreads();
        Read();

        void Read() => a[0] = p[0];
    }

    // C2, GSH0041: the gap after the if / else lies only on the edges into the join
    [GpuKernel(Cooperative = true)]
    public static unsafe void EdgeGapOnlyKernel(int[] a)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int* p = stackalloc int[4];
        p[0] = tid;
        Gpu.SyncGrid(() =>
        {
            if (tid > 0) { a[tid] = p[0]; } else { a[tid] = -p[0]; }
            a[tid] += p[0];
        });
    }

    // R, GSH0041: the gap after the loop lies only on its exit edge
    [GpuKernel(Cooperative = true)]
    public static unsafe void LoopExitGapKernel(int[] a)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int* p = stackalloc int[4];
        p[0] = tid;
        Gpu.SyncGrid(() =>
        {
            for (int k = 0; k < 2; k++)
            {
                a[tid] += k;
            }

            a[tid] += p[0];
        });
    }

    // A, no diagnostic: the span belongs to the lambda run after the barrier
    [GpuKernel]
    public static void SpanInLambdaKernel(int[] a, int n)
    {
        Gpu.SyncThreads();
        Gpu.GridStrideLoop(n, i =>
        {
            Span<int> s = stackalloc int[4];
            s[0] = i;
            a[i] = s[0];
        });
    }

    // A2, no diagnostic: the span belongs to the local function called after the barrier
    [GpuKernel]
    public static void SpanInLocalFunctionKernel(int[] a)
    {
        Gpu.SyncThreads();
        Fill();

        void Fill()
        {
            Span<int> s = stackalloc int[4];
            s[0] = 1;
            a[0] = s[0];
        }
    }

    // B, GSH0039: the shuffle in the value runs before the span of the target is used
    [GpuKernel]
    public static void ShuffleInValueKernel(int[] buffer)
    {
        Span<int> span = buffer;
        span[0] = Warp.ShuffleDownSync(0xFFFFFFFFu, span[1], 1u);
    }

    // No diagnostic: the out argument assigns the span again after the barrier, before it is used
    [GpuKernel]
    public static void OutArgumentAfterBarrierKernel(int[] buffer)
    {
        Span<int> s = buffer;
        s[0] = 1;
        Gpu.SyncThreads();
        Fill(buffer, out s);
        s[1] = 2;
    }

    [GpuDeviceFunction]
    public static void Fill(int[] b, out Span<int> s) => s = b;

    // GSH0041: the delegate stored before the barrier runs after it and reads the captured pointer (PR #771 item 1)
    [GpuKernel]
    public static unsafe void LambdaCapturedStackallocKernel(int[] result)
    {
        int tid = ThreadIndex.X;
        int* p = stackalloc int[4];
        p[0] = tid + 1;
        Func<int> f = () => p[0];
        Gpu.SyncThreads();
        result[tid] = f();
    }

    // No diagnostic: the delegate runs only before the barrier, and nothing after it uses the pointer
    [GpuKernel]
    public static unsafe void LambdaInvokedBeforeBarrierKernel(int[] result)
    {
        int tid = ThreadIndex.X;
        int* p = stackalloc int[4];
        p[0] = tid + 1;
        Func<int> f = () => p[0];
        result[tid] = f();
        Gpu.SyncThreads();
        result[tid] += 10;
    }

    // GSH0041: the delegate is reassigned after the barrier, but the one stored before it runs first
    [GpuKernel]
    public static unsafe void LambdaInvokedBeforeReassignmentKernel(int[] result)
    {
        int* p = stackalloc int[4];
        Func<int> f = () => p[0];
        Gpu.SyncThreads();
        result[0] = f();
        f = () => 2;
        result[1] = f();
    }

    // No diagnostic: the delegate is assigned again after the barrier before it runs
    [GpuKernel]
    public static unsafe void LambdaReassignedAfterBarrierKernel(int[] result)
    {
        int* p = stackalloc int[4];
        Func<int> f = () => p[0];
        result[0] = f();
        Gpu.SyncThreads();
        f = () => 2;
        result[1] = f();
    }

    // GSH0041: the delegate is copied into another local, which runs after the barrier
    [GpuKernel]
    public static unsafe void CopiedDelegateKernel(int[] result)
    {
        int* p = stackalloc int[4];
        Func<int> f = () => p[0];
        Func<int> g = f;
        Gpu.SyncThreads();
        result[0] = g();
    }

    // GSH0041: a local function converted to a delegate before the barrier runs after it
    [GpuKernel]
    public static unsafe void LocalFunctionDelegateKernel(int[] result)
    {
        int* p = stackalloc int[4];
        Func<int> f = Read;
        Gpu.SyncThreads();
        result[0] = f();

        int Read() => p[0];
    }

    // GSH0041: the delegate made by a lambda that runs before the barrier is stored and runs after it
    [GpuKernel]
    public static unsafe void NestedLambdaKernel(int[] result)
    {
        int* p = stackalloc int[4];
        Func<Func<int>> make = () => () => p[0];
        Func<int> f = make();
        Gpu.SyncThreads();
        result[0] = f();
    }

    // No diagnostic: the body of Gpu.SingleThread runs where it is called, before the barrier
    [GpuKernel]
    public static unsafe void MacroBodyBeforeBarrierKernel(int[] result)
    {
        int* p = stackalloc int[4];
        p[0] = 1;
        Gpu.SingleThread(() => { result[0] = p[0]; });
        Gpu.SyncThreads();
        result[1] = 2;
    }
}
