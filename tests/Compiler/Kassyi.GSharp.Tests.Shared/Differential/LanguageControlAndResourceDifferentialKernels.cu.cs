#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct DiffUnrollContext
{
    public int Base;
    public int Acc;
}

public struct DiffLifoOrderRecorder : IDisposable
{
    public int[] Log;
    public int Slot;
    public int Id;

    public void Dispose()
    {
        Log[Slot] = (Log[Slot] * 10) + Id;
    }
}

public ref struct DiffPatternRefRecorder
{
    public int[] Log;
    public int Slot;
    public int Marker;

    public void Dispose()
    {
        Log[Slot] += Marker;
    }
}

/// <summary>
/// A disposable struct used in a <c>using (exprGuard)</c> expression statement to verify that C# and G# dispose the
/// snapshot copy taken on entry even when the original variable is mutated inside the <c>using</c> block.
/// </summary>
public struct DiffMutatingDisposeGuard : IDisposable
{
    public int[] Log;
    public int Slot;
    public int State;

    public void Dispose()
    {
        Log[Slot] = State;
    }
}

public enum DiffSortPriority : short
{
    High = 15,
    Low = -9,
    Medium = 0,
    Urgent = 42,
}

public struct DiffComparableItem : IComparable<DiffComparableItem>
{
    public int Key;
    public int Payload;

    public int CompareTo(DiffComparableItem other) =>
        Key < other.Key ? -1 : (Key > other.Key ? 1 : 0);
}

public struct DiffDescendingIntComparer : IComparer<int>
{
    public int Compare(int a, int b) =>
        a > b ? -1 : (a < b ? 1 : 0);
}

[InlineArray(4)]
public struct DiffInlineInt4
{
    private int _element0;
}

public unsafe struct DiffFixedInt4
{
    public fixed int Buf[4];
}

/// <summary>
/// Differential kernels for <c>Gpu.ForUnroll</c>, device iterators (<c>yield return</c> / <c>yield break</c>),
/// local functions, <c>using</c> disposal lifecycle, <c>MemoryExtensions.Sort</c>,
/// <c>[InlineArray]</c>, <c>fixed</c> buffers, collection expressions, named/optional arguments, and <c>ref</c> returns.
/// </summary>
public static partial class LanguageControlAndResourceDifferentialKernels
{
    [GpuDeviceFunction]
    public static int Encode4(Span<int> s) => (s[0] * 1000) + (s[1] * 100) + (s[2] * 10) + s[3];

    [GpuDeviceFunction]
    public static int Encode4(int a, int b, int c, int d) => (a * 1000) + (b * 100) + (c * 10) + d;

    [GpuDeviceFunction]
    public static IEnumerable<int> YieldSequenceWithEarlyBreak(int baseVal, int stopThreshold)
    {
        yield return baseVal + 1;
        yield return baseVal + 2;
        if (stopThreshold <= 2)
        {
            yield break;
        }

        yield return baseVal + 3;
        yield return baseVal + 4;
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> YieldPairWithDefault(int first, int step = 10)
    {
        yield return first;
        yield return first + step;
    }

    [GpuDeviceFunction]
    public static int RunUsingEarlyReturnHelper(int[] log, int slot, int x, bool takeEarlyReturn)
    {
        using var outer = new DiffLifoOrderRecorder { Log = log, Slot = slot, Id = 1 };
        using var inner = new DiffLifoOrderRecorder { Log = log, Slot = slot, Id = 2 };
        if (takeEarlyReturn)
        {
            return x + 10;
        }

        return x + 20;
    }

    [GpuDeviceFunction]
    public static int CompareAscendingStrict(int a, int b) =>
        a < b ? -1 : (a > b ? 1 : 0);

    [GpuDeviceFunction]
    public static int StepOrder(ref int trace, int stepId, int value)
    {
        trace = (trace * 10) + stepId;
        return value;
    }

    [GpuDeviceFunction]
    public static int EvalNamedOrder(int a, int b, int c) =>
        (a * 100) + (b * 10) + c;

    [GpuDeviceFunction]
    public static int EvalOptionalDefaults(int x, int scale = 3, int bias = 17) =>
        (x * scale) + bias;

    [GpuDeviceFunction]
    public static ref int SelectElementByRef(Span<int> span, bool selectSecond)
    {
        if (selectSecond)
        {
            return ref span[1];
        }

        return ref span[0];
    }

    // ---------------------------------------------------------
    // ForUnroll and Device Iterators (10 individual kernels)
    // ---------------------------------------------------------

    [GpuKernel]
    public static void ForUnrollPositiveRangeKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int posSum = 0;
        Gpu.ForUnroll(0, 4, i => { posSum += (x + i) * (i + 1); });
        r[tid] = posSum;
    }

    [GpuKernel]
    public static void ForUnrollNegativeStartKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int y = ys[tid] & 0x3FF;
        int negSum = 0;
        Gpu.ForUnroll(-2, 4, i => { negSum += (y * i) + 5; });
        r[tid] = negSum;
    }

    [GpuKernel]
    public static void ForUnrollZeroCountKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int untouched = 42;
        Gpu.ForUnroll(0, 0, i => { untouched += 999 + i; });
        r[tid] = untouched;
    }

    [GpuKernel]
    public static void ForUnrollRefContextKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        var ctx = new DiffUnrollContext { Base = x & 15, Acc = 1 };
        Gpu.ForUnroll(ref ctx, 1, 3, (ref DiffUnrollContext c, int i) =>
        {
            c.Acc = (c.Acc * 3) + i + c.Base;
        });
        r[tid] = ctx.Acc;
    }

    [GpuKernel]
    public static void ForUnrollNestedKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int nestedUnroll = 0;
        Gpu.ForUnroll(0, 2, outer =>
        {
            Gpu.ForUnroll(0, 3, inner =>
            {
                nestedUnroll += ((outer + 1) * 10) + inner;
            });
        });
        r[tid] = nestedUnroll;
    }

    [GpuKernel]
    public static void DeviceIteratorEarlyBreakKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        int stop = (y & 1) == 0 ? 2 : 4;
        int earlyBreakSum = 0;
        foreach (int v in YieldSequenceWithEarlyBreak(x, stop))
        {
            earlyBreakSum += v;
        }
        r[tid] = earlyBreakSum;
    }

    [GpuKernel]
    public static void DeviceIteratorConsecutiveExpansionsKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        int firstPass = 0;
        foreach (int v in YieldSequenceWithEarlyBreak(x, 4))
        {
            firstPass += v;
        }
        int secondPass = 0;
        foreach (int v in YieldSequenceWithEarlyBreak(y, 2))
        {
            secondPass += v * 2;
        }
        r[tid] = firstPass + secondPass;
    }

    [GpuKernel]
    public static void DeviceIteratorConsumerBreakContinueKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int consumerFlow = 0;
        int idx = 0;
        foreach (int v in YieldSequenceWithEarlyBreak(x, 4))
        {
            idx++;
            if (idx == 2)
            {
                continue;
            }
            consumerFlow = (consumerFlow * 10) + v;
            if (idx == 3)
            {
                break;
            }
        }
        r[tid] = consumerFlow;
    }

    [GpuKernel]
    public static void DeviceIteratorNestedExpansionKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int nestedIter = 0;
        foreach (int outerVal in YieldPairWithDefault(x, 5))
        {
            foreach (int innerVal in YieldPairWithDefault(outerVal, 2))
            {
                nestedIter += innerVal;
            }
        }
        r[tid] = nestedIter;
    }

    [GpuKernel]
    public static void DeviceIteratorOptionalParamDefaultKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int optIter = 0;
        foreach (int v in YieldPairWithDefault(x))
        {
            optIter += v;
        }
        r[tid] = optIter;
    }

    // ---------------------------------------------------------
    // Local Functions and Using Lifecycle (12 individual kernels)
    // ---------------------------------------------------------

    [GpuKernel]
    public static void StaticLocalFunctionKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        static int StaticBlend(int u, int v) => (u * 3) - v + 7;
        r[tid] = StaticBlend(x, y);
    }

    [GpuKernel]
    public static void CapturingLocalFunctionReturnKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        int capturedBias = y + 3;
        int capturedAcc = 10;
        int CaptureAndMutate(int delta)
        {
            capturedAcc += delta + capturedBias;
            return capturedAcc * 2;
        }
        int capRet1 = CaptureAndMutate(x);
        int capRet2 = CaptureAndMutate(5);
        r[tid] = capRet1 + capRet2;
    }

    [GpuKernel]
    public static void CapturingLocalFunctionMutatedOuterKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        int capturedBias = y + 3;
        int capturedAcc = 10;
        int CaptureAndMutate(int delta)
        {
            capturedAcc += delta + capturedBias;
            return capturedAcc * 2;
        }
        CaptureAndMutate(x);
        CaptureAndMutate(5);
        r[tid] = capturedAcc;
    }

    [GpuKernel]
    public static void RefOutLocalFunctionStateKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        static void MutateRefAndSetOut(ref int state, int delta, out int before)
        {
            before = state;
            state = (state * 2) + delta;
        }
        int refState = x + 1;
        MutateRefAndSetOut(ref refState, y, out int dummy);
        r[tid] = refState;
    }

    [GpuKernel]
    public static void RefOutLocalFunctionSnapshotKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        static void MutateRefAndSetOut(ref int state, int delta, out int before)
        {
            before = state;
            state = (state * 2) + delta;
        }
        int refState = x + 1;
        MutateRefAndSetOut(ref refState, y, out int refBefore);
        r[tid] = refBefore;
    }

    [GpuKernel]
    public static void BlockLambdaReturnKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        Func<int, int, int> blockLambda = (u, v) =>
        {
            int sum = u + v;
            int diff = u - v;
            return (sum * 3) + diff;
        };
        r[tid] = blockLambda(x, y);
    }

    [GpuKernel]
    public static void UsingLifoOrderKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        r[tid] = 0;
        using (var u1 = new DiffLifoOrderRecorder { Log = r, Slot = tid, Id = 1 })
        using (var u2 = new DiffLifoOrderRecorder { Log = r, Slot = tid, Id = 2 })
        {
            using (var u3 = new DiffLifoOrderRecorder { Log = r, Slot = tid, Id = 3 })
            {
                int sumIds = u1.Id + u2.Id + u3.Id;
                if (sumIds < 0)
                {
                    r[tid] = -1;
                }
            }
        }
    }

    [GpuKernel]
    public static void UsingEarlyReturnAndOrderKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        r[tid] = 0;
        int helperRet = RunUsingEarlyReturnHelper(r, tid, x, (y & 1) != 0);
        r[tid] = (r[tid] * 1000) + helperRet;
    }

    [GpuKernel]
    public static void UsingLoopBreakContinueTraceKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        r[tid] = 0;
        for (int i = 1; i <= 4; i++)
        {
            using (var loopGuard = new DiffLifoOrderRecorder { Log = r, Slot = tid, Id = i })
            {
                if (i == 2)
                {
                    continue;
                }

                if (i == 3)
                {
                    break;
                }
            }
        }
    }

    [GpuKernel]
    public static void UsingPatternRefStructKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        r[tid] = 100;
        using (var patGuard = new DiffPatternRefRecorder { Log = r, Slot = tid, Marker = x + 5 })
        {
            r[tid] += 10;
        }
    }

    [GpuKernel]
    public static void UsingMutatingGuardGetterValKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        var exprGuard = new DiffMutatingDisposeGuard { Log = r, Slot = tid, State = x };
        int mutGetterRead;
        using (exprGuard)
        {
            exprGuard.State = x + 100;
            mutGetterRead = exprGuard.State;
        }
        r[tid] = mutGetterRead;
    }

    [GpuKernel]
    public static void UsingMutatingGuardDisposedStateKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        r[tid] = 0;
        var exprGuard = new DiffMutatingDisposeGuard { Log = r, Slot = tid, State = x };
        using (exprGuard)
        {
            exprGuard.State = x + 100;
        }
    }

    // ---------------------------------------------------------
    // SpanSort and Modern Syntax (12 individual kernels)
    // ---------------------------------------------------------

    [GpuKernel]
    public static void SpanSortIntKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x7F;
        int k0 = (x * 4) + 2;
        int k1 = (x * 4) + 0;
        int k2 = (x * 4) + 3;
        int k3 = (x * 4) + 1;
        Span<int> sInt = [k0, k1, k2, k3];
        sInt.Sort();
        r[tid] = Encode4(sInt);
    }

    [GpuKernel]
    public static void SpanSortEnumKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        Span<DiffSortPriority> sEnum =
        [
            DiffSortPriority.Urgent,
            DiffSortPriority.Low,
            DiffSortPriority.High,
            DiffSortPriority.Medium,
        ];
        sEnum.Sort();
        r[tid] = Encode4((int)sEnum[0], (int)sEnum[1], (int)sEnum[2], (int)sEnum[3]);
    }

    [GpuKernel]
    public static void SpanSortComparableStructKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        Span<DiffComparableItem> sComp = stackalloc DiffComparableItem[4];
        sComp[0] = new DiffComparableItem { Key = 30, Payload = 3 };
        sComp[1] = new DiffComparableItem { Key = 10, Payload = 1 };
        sComp[2] = new DiffComparableItem { Key = 40, Payload = 4 };
        sComp[3] = new DiffComparableItem { Key = 20, Payload = 2 };
        sComp.Sort();
        r[tid] = Encode4(sComp[0].Payload, sComp[1].Payload, sComp[2].Payload, sComp[3].Payload);
    }

    [GpuKernel]
    public static void SpanSortComparerKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x7F;
        int k0 = (x * 4) + 2;
        int k1 = (x * 4) + 0;
        int k2 = (x * 4) + 3;
        int k3 = (x * 4) + 1;
        Span<int> sDesc = [k0, k1, k2, k3];
        sDesc.Sort(new DiffDescendingIntComparer());
        r[tid] = Encode4(sDesc);
    }

    [GpuKernel]
    public static void SpanSortMethodGroupComparisonKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x7F;
        int k0 = (x * 4) + 2;
        int k1 = (x * 4) + 0;
        int k2 = (x * 4) + 3;
        int k3 = (x * 4) + 1;
        Span<int> sMg = [k0, k1, k2, k3];
        sMg.Sort(CompareAscendingStrict);
        r[tid] = Encode4(sMg);
    }

    [GpuKernel]
    public static void SpanSortKeyValueDefaultKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x7F;
        int k0 = (x * 4) + 2;
        int k1 = (x * 4) + 0;
        int k2 = (x * 4) + 3;
        int k3 = (x * 4) + 1;
        Span<int> kvKeys1 = [k0, k1, k2, k3];
        Span<int> kvItems1 = [1, 2, 3, 4];
        MemoryExtensions.Sort(kvKeys1, kvItems1);
        r[tid] = Encode4(kvItems1);
    }

    [GpuKernel]
    public static void SpanSortKeyValueComparerKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x7F;
        int k0 = (x * 4) + 2;
        int k1 = (x * 4) + 0;
        int k2 = (x * 4) + 3;
        int k3 = (x * 4) + 1;
        Span<int> kvKeys2 = [k0, k1, k2, k3];
        Span<int> kvItems2 = [1, 2, 3, 4];
        kvKeys2.Sort(kvItems2, new DiffDescendingIntComparer());
        r[tid] = Encode4(kvItems2);
    }

    [GpuKernel]
    public static void SpanSortKeyValueLambdaKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x7F;
        int k0 = (x * 4) + 2;
        int k1 = (x * 4) + 0;
        int k2 = (x * 4) + 3;
        int k3 = (x * 4) + 1;
        Span<int> kvKeys3 = [k0, k1, k2, k3];
        Span<int> kvItems3 = [1, 2, 3, 4];
        kvKeys3.Sort(kvItems3, (a, b) => a < b ? -1 : (a > b ? 1 : 0));
        r[tid] = Encode4(kvItems3);
    }

    [GpuKernel]
    public static unsafe void InlineArrayAndFixedBufferKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x7F;
        int y = ys[tid] & 0x7F;
        DiffInlineInt4 inlineBuf = default;
        inlineBuf[0] = x + 1;
        inlineBuf[1] = x + 2;
        inlineBuf[2] = x + 3;
        inlineBuf[3] = x + 4;
        int inlineSum = 0;
        foreach (int item in inlineBuf)
        {
            inlineSum += item;
        }

        DiffFixedInt4 fixedStruct = default;
        fixedStruct.Buf[0] = y + 10;
        fixedStruct.Buf[1] = y + 20;
        fixedStruct.Buf[2] = y + 30;
        fixedStruct.Buf[3] = y + 40;
        int fixedSum = fixedStruct.Buf[0] + fixedStruct.Buf[1] + fixedStruct.Buf[2] + fixedStruct.Buf[3];
        r[tid] = inlineSum + fixedSum;
    }

    [GpuKernel]
    public static void CollectionExpressionSpreadKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x7F;
        DiffInlineInt4 inlineBuf = default;
        inlineBuf[1] = x + 2;
        inlineBuf[2] = x + 3;
        ReadOnlySpan<int> emptySlice = [];
        Span<int> spreadSpan = [100 + emptySlice.Length, inlineBuf[1], inlineBuf[2], 200];
        r[tid] = spreadSpan[0] + (spreadSpan[1] * 10) + (spreadSpan[2] * 100) + (spreadSpan[3] * 1000);
    }

    [GpuKernel]
    public static void NamedArgumentsEvaluationOrderKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x7F;
        int y = ys[tid] & 0x7F;
        int argTrace = 0;
        int namedVal = EvalNamedOrder(
            c: StepOrder(ref argTrace, 1, x & 7),
            a: StepOrder(ref argTrace, 2, (y & 7) + 1),
            b: StepOrder(ref argTrace, 3, 5));
        r[tid] = (namedVal * 1000) + argTrace;
    }

    [GpuKernel]
    public static void OptionalArgumentsAndRefReturnKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x7F;
        Span<int> spreadSpan = [100, x + 2, x + 3, 200];
        int opt1 = EvalOptionalDefaults(x);
        int opt2 = EvalOptionalDefaults(x, scale: 5);
        int opt3 = EvalOptionalDefaults(x, bias: 3, scale: 2);
        SelectElementByRef(spreadSpan, (x & 1) != 0) += 77;
        int refResult = spreadSpan[0] + spreadSpan[1];
        r[tid] = opt1 + opt2 + opt3 + refResult;
    }
}
