using System.Diagnostics;
using Kassyi.GSharp.Core;

namespace IrLinear;

public static class IrLinearCases
{
    public static void ElseBarrier(int[] a, bool c)
    {
        if (c) { int y = a[0]; a[1] = y; }
        else { Gpu.SyncThreads(); }
    }

    public static void NoSplitIf(int[] a, bool c)
    {
        if (c) { a[0] = 1; }
        Gpu.SyncThreads();
    }

    public static void WhileBarrier(int[] a, int n)
    {
        int i = 0;
        while (i < n) { a[i] = i; Gpu.SyncThreads(); i++; }
    }

    public static void Numbering(int[] a, bool c, bool d, int n)
    {
        int i = 0;
        if (c) { if (d) { Gpu.SyncThreads(); } }
        while (i < n) { if (d) { Gpu.SyncThreads(); } i++; }
    }

    public static void ShuffleCondition(int v, int i)
    {
        while (Warp.ShuffleSync(0xFFFFFFFFu, v, 0) > i) { i++; }
    }

    public static void ShuffleConditionWithBodySplit(int v, int i)
    {
        while (Warp.ShuffleSync(0xFFFFFFFFu, v, 0) > i) { int w = Warp.ShuffleSync(0xFFFFFFFFu, v, 0) + 1; i += w; }
    }

    public static void AndCondition(bool c, bool d, int v)
    {
        while (c && Warp.ShuffleSync(0xFFFFFFFFu, v, 0) > 0) { if (d) { Gpu.SyncThreads(); } }
    }

    public static void TwoAndConditions(bool c, int v)
    {
        while (c && Warp.ShuffleSync(0xFFFFFFFFu, v, 0) > 0) { v++; }
        while (c && Warp.ShuffleSync(0xFFFFFFFFu, v, 1) > 0) { v--; }
    }

    public static void NestedWhiles(int[] a, int n)
    {
        int i = 0;
        while (i < n)
        {
            int j = 0;
            while (j < n) { Gpu.SyncThreads(); j++; }
            i++;
        }
    }

    public static void ElseIfAfterSplit(int[] a, bool c, bool d)
    {
        if (c) { Gpu.SyncThreads(); }
        else if (d) { a[0] = 1; }
    }

    public static void SplitInElseIf(int[] a, bool c, bool d)
    {
        if (c) { a[0] = 1; }
        else if (d) { Gpu.SyncThreads(); }
        else { }
    }

    public static void ParenthesizedCompound(int[] a, int i, int v)
    {
        int x = (a[i] += Warp.ShuffleSync(0xFFFFFFFFu, v, 0));
        a[0] = x;
    }

    public static void Commented(int[] a, int v)
    {
        // note
        a[0] = Warp.ShuffleSync(0xFFFFFFFFu, v, 0) + a[1];
    }

    public static void RemovedCallAroundShuffle(bool c, int v)
    {
        if (c) Trace(Warp.ShuffleSync(0xFFFFFFFFu, v, 0));
    }

    public static void BreakInLoop(bool d, int n)
    {
        int i = 0;
        while (i < n) { if (d) break; Gpu.SyncThreads(); i++; }
    }

    public static void ForWithBarrier(int n)
    {
        for (int k = 0; k < n; k++) { Gpu.SyncThreads(); }
    }

    [Conditional("IR_LINEAR_CASES_NEVER_DEFINED")]
    private static void Trace(int v) { }
}
