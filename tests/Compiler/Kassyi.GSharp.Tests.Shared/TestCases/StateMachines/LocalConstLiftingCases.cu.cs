using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.StateMachines;

// Local consts of bodies that the CPU state machine splits: the lowering declares every const of the body in front of
// its state switch. The case label kernels run only on the managed path, from an in-memory build: the native C++ writes
// a local const as a variable, which a C++ case label cannot name (C2051, also without a barrier)
[SkipSnapshotConsistency("Lowering and managed execution cases")]
public static partial class LocalConstLiftingCases
{
    [GpuKernel]
    public static void ConstBeforeBarrier(uint[] r)
    {
        const int D = 2;
        int t = ThreadIndex.X;
        Gpu.SyncThreads();
        uint x = D;
        r[t] = x;
    }

    [GpuKernel]
    public static void ConstsAndLocalFunction(int[] r)
    {
        const int A = 1;
        int F() => A;
        const int B = A + 1;
        int t = ThreadIndex.X;
        Gpu.SyncThreads();
        r[t] = F() + B;
    }

    [GpuKernel]
    public static void ConstInsideLocalFunctionAndLambda(int[] r)
    {
        int t = ThreadIndex.X;

        int F(int v)
        {
            const int K = 7;
            return v + K;
        }

        Func<int, int> g = v =>
        {
            const int L = 1;
            return v + L;
        };

        Gpu.SyncThreads();
        r[t] = F(t) + g(0);
    }

    [GpuKernel]
    public static void ConstWithoutBarrier(uint[] r)
    {
        const int D = 2;
        int t = ThreadIndex.X;
        uint x = D;
        r[t] = x;
    }

    [GpuKernel]
    public static void SiblingConstsOfOneName(int[] r)
    {
        int t = ThreadIndex.X;
        {
            const int K = 3;
            Gpu.SyncThreads();
            r[t] = K;
        }
        {
            const int K = 4;
            Gpu.SyncThreads();
            r[t] += K;
        }
    }

    // A case label after the barrier names the const
    [GpuKernel]
    public static void CaseLabelConstAcrossBarrier(int[] r)
    {
        const int D = 2;
        int t = ThreadIndex.X;
        Gpu.SyncThreads();
        switch (t)
        {
            case D:
                r[t] = 1;
                break;
            default:
                r[t] = 0;
                break;
        }
    }

    // goto case names a const that the managed output renames, as a sibling scope declares its name too
    [GpuKernel]
    public static void GotoCaseRenamedConst(int[] r)
    {
        int t = ThreadIndex.X;
        {
            const int K = 3;
            Gpu.SyncThreads();
            r[t] = K;
        }
        {
            const int K = 1;
            Gpu.SyncThreads();
            switch (t & 1)
            {
                case 0:
                    goto case K;
                case K:
                    r[t] += 2;
                    break;
            }
        }
    }

    // Constant patterns after the barrier name the const (the native C++ does not translate these patterns)
    [GpuKernel]
    public static void ConstPatternsAcrossBarrier(int[] r)
    {
        const int Lim = 1;
        int t = ThreadIndex.X;
        Gpu.SyncThreads();
        switch (t)
        {
            case < Lim:
                r[t * 3] = 1;
                break;
            default:
                r[t * 3] = 2;
                break;
        }

        r[t * 3 + 1] = t is < Lim ? 1 : 2;
        r[t * 3 + 2] = t is Lim ? 7 : 8;
    }

    // Not a kernel: the goto forms that IsNonValueIdentifierPosition classifies
    public static void GotoLabelAndGotoCase(int[] r, int t)
    {
        const int K = 1;
        if (t < 0)
        {
            goto Done;
        }

        switch (t & 1)
        {
            case 0:
                goto case K;
            case K:
                r[0] = 1;
                break;
        }

    Done:
        r[1] = 2;
    }

    public static void RunCaseLabelConstAcrossBarrier(int[] r, uint blockDimX) =>
        LaunchCaseLabelConstAcrossBarrier(r, blockDimX: blockDimX);

    public static void RunGotoCaseRenamedConst(int[] r, uint blockDimX) =>
        LaunchGotoCaseRenamedConst(r, blockDimX: blockDimX);

    public static void RunConstPatternsAcrossBarrier(int[] r, uint blockDimX) =>
        LaunchConstPatternsAcrossBarrier(r, blockDimX: blockDimX);
}
