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
/// local functions, <c>using</c> disposal lifecycle (and S41), <c>MemoryExtensions.Sort</c>,
/// <c>[InlineArray]</c>, <c>fixed</c> buffers, collection expressions, named/optional arguments, and <c>ref</c> returns
/// (Lane D6 Stage 3).
/// </summary>
public static partial class LanguageControlAndResourceDifferentialKernels
{
    public static readonly string[] ForUnrollAndIteratorsColumns =
    [
        "unroll_pos_sum",
        "unroll_neg_start",
        "unroll_zero_count_untouched",
        "unroll_ref_ctx_acc",
        "unroll_nested_acc",
        "iter_early_break_sum",
        "iter_two_consecutive_expansions",
        "iter_consumer_break_continue",
        "iter_nested_expansion",
        "iter_optional_param_default",
    ];

    public static readonly string[] LocalFunctionsAndUsingColumns =
    [
        "static_local_fn",
        "capturing_local_fn_ret",
        "capturing_local_fn_mutated_outer",
        "ref_out_local_fn_state",
        "ref_out_local_fn_snap",
        "block_lambda_ret",
        "using_lifo_321",
        "using_early_return_and_order",
        "using_loop_break_continue_trace",
        "using_pattern_ref_struct",
        "using_mut_guard_getter_val",
        "using_mut_guard_disposed_state",
    ];

    public static readonly string[] SpanSortAndModernSyntaxColumns =
    [
        "sort_int_asc",
        "sort_enum_signed",
        "sort_comparable_struct_payloads",
        "sort_comparer_desc",
        "sort_method_group_comparison",
        "sort_kv_default_items",
        "sort_kv_comparer_items",
        "sort_kv_lambda_items",
        "inline_array_and_fixed_buf",
        "collection_expr_spread",
        "named_args_val_and_eval_order",
        "optional_args_and_ref_return",
    ];

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

    [GpuKernel]
    public static void ForUnrollAndDeviceIteratorsKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        int o = tid * 10;

        // 1. Gpu.ForUnroll: positive range, negative start, zero count, ref context, and nested ForUnroll
        int posSum = 0;
        Gpu.ForUnroll(0, 4, i =>
        {
            posSum += (x + i) * (i + 1);
        });

        int negSum = 0;
        Gpu.ForUnroll(-2, 4, i =>
        {
            negSum += (y * i) + 5;
        });

        int untouched = 42;
        Gpu.ForUnroll(0, 0, i =>
        {
            untouched += 999 + i;
        });

        var ctx = new DiffUnrollContext { Base = x & 15, Acc = 1 };
        Gpu.ForUnroll(ref ctx, 1, 3, (ref c, i) =>
        {
            c.Acc = (c.Acc * 3) + i + c.Base;
        });

        int nestedUnroll = 0;
        Gpu.ForUnroll(0, 2, outer =>
        {
            Gpu.ForUnroll(0, 3, inner =>
            {
                nestedUnroll += ((outer + 1) * 10) + inner;
            });
        });

        // 2. Device iterators: early yield break, two consecutive expansions in same scope, consumer break/continue, nested
        int stop = (y & 1) == 0 ? 2 : 4;
        int earlyBreakSum = 0;
        foreach (int v in YieldSequenceWithEarlyBreak(x, stop))
        {
            earlyBreakSum += v;
        }

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

        int nestedIter = 0;
        foreach (int outerVal in YieldPairWithDefault(x, 5))
        {
            foreach (int innerVal in YieldPairWithDefault(outerVal, 2))
            {
                nestedIter += innerVal;
            }
        }

        int optIter = 0;
        foreach (int v in YieldPairWithDefault(x))
        {
            optIter += v;
        }

        r[o + 0] = posSum;
        r[o + 1] = negSum;
        r[o + 2] = untouched;
        r[o + 3] = ctx.Acc;
        r[o + 4] = nestedUnroll;
        r[o + 5] = earlyBreakSum;
        r[o + 6] = firstPass + secondPass;
        r[o + 7] = consumerFlow;
        r[o + 8] = nestedIter;
        r[o + 9] = optIter;
    }

    [GpuKernel]
    public static void LocalFunctionsAndUsingLifecycleKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        int o = tid * 12;

        static int StaticBlend(int u, int v) => (u * 3) - v + 7;

        int capturedBias = y + 3;
        int capturedAcc = 10;
        int CaptureAndMutate(int delta)
        {
            capturedAcc += delta + capturedBias;
            return capturedAcc * 2;
        }

        static void MutateRefAndSetOut(ref int state, int delta, out int before)
        {
            before = state;
            state = (state * 2) + delta;
        }

        int staticLocalRes = StaticBlend(x, y);
        int capRet1 = CaptureAndMutate(x);
        int capRet2 = CaptureAndMutate(5);

        int refState = x + 1;
        MutateRefAndSetOut(ref refState, y, out int refBefore);

        Func<int, int, int> blockLambda = (u, v) =>
        {
            int sum = u + v;
            int diff = u - v;
            return (sum * 3) + diff;
        };
        int lambdaRes = blockLambda(x, y);

        // 1. 3-level nested using statements dispose in strict LIFO order (3 -> 2 -> 1 => 321)
        r[o + 6] = 0;
        using (var u1 = new DiffLifoOrderRecorder { Log = r, Slot = o + 6, Id = 1 })
        using (var u2 = new DiffLifoOrderRecorder { Log = r, Slot = o + 6, Id = 2 })
        {
            using (var u3 = new DiffLifoOrderRecorder { Log = r, Slot = o + 6, Id = 3 })
            {
                int sumIds = u1.Id + u2.Id + u3.Id;
                if (sumIds < 0)
                {
                    r[o + 6] = -1;
                }
            }
        }

        // 2. Early return inside helper disposes using var declarations in reverse order (2 -> 1 => 21)
        r[o + 7] = 0;
        int helperRet = RunUsingEarlyReturnHelper(r, o + 7, x, (y & 1) != 0);
        r[o + 7] = (r[o + 7] * 1000) + helperRet;

        // 3. using inside loop with continue and break disposes on every iteration
        r[o + 8] = 0;
        for (int i = 1; i <= 4; i++)
        {
            using (var loopGuard = new DiffLifoOrderRecorder { Log = r, Slot = o + 8, Id = i })
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

        // 4. Pattern-based ref struct Dispose() (no IDisposable interface)
        r[o + 9] = 100;
        using (var patGuard = new DiffPatternRefRecorder { Log = r, Slot = o + 9, Marker = x + 5 })
        {
            r[o + 9] += 10;
        }

        // 5. using (expression) copy semantics: mutating the source variable inside the block does not alter the disposed copy
        var exprGuard = new DiffMutatingDisposeGuard { Log = r, Slot = o + 11, State = x };
        int mutGetterRead;
        using (exprGuard)
        {
            exprGuard.State = x + 100;
            mutGetterRead = exprGuard.State;
        }

        r[o + 0] = staticLocalRes;
        r[o + 1] = capRet1 + capRet2;
        r[o + 2] = capturedAcc;
        r[o + 3] = refState;
        r[o + 4] = refBefore;
        r[o + 5] = lambdaRes;
        // r[o + 6..9] populated above
        r[o + 10] = mutGetterRead;
        // r[o + 11] written by mutGuard.Dispose()
    }

    [GpuKernel]
    public static unsafe void SpanSortAndModernSyntaxKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid] & 0x7F;
        int y = ys[tid] & 0x7F;
        int o = tid * 12;

        // Distinct 4-element keys so even an unstable sort produces a unique total order
        int k0 = (x * 4) + 2;
        int k1 = (x * 4) + 0;
        int k2 = (x * 4) + 3;
        int k3 = (x * 4) + 1;

        // 1. Span<int>.Sort()
        Span<int> sInt = [k0, k1, k2, k3];
        sInt.Sort();
        int sortIntEncoded = (sInt[0] * 1000) + (sInt[1] * 100) + (sInt[2] * 10) + sInt[3];

        // 2. Span<Enum>.Sort() with signed short underlying type
        Span<DiffSortPriority> sEnum =
        [
            DiffSortPriority.Urgent,
            DiffSortPriority.Low,
            DiffSortPriority.High,
            DiffSortPriority.Medium,
        ];
        sEnum.Sort();
        int sortEnumEncoded = ((int)sEnum[0] * 1000) + ((int)sEnum[1] * 100) + ((int)sEnum[2] * 10) + (int)sEnum[3];

        // 3. Span<IComparable<T>>.Sort()
        Span<DiffComparableItem> sComp = stackalloc DiffComparableItem[4];
        sComp[0] = new DiffComparableItem { Key = 30, Payload = 3 };
        sComp[1] = new DiffComparableItem { Key = 10, Payload = 1 };
        sComp[2] = new DiffComparableItem { Key = 40, Payload = 4 };
        sComp[3] = new DiffComparableItem { Key = 20, Payload = 2 };
        sComp.Sort();
        int sortComparablePayloads = (sComp[0].Payload * 1000) + (sComp[1].Payload * 100) + (sComp[2].Payload * 10) + sComp[3].Payload;

        // 4. Span<int>.Sort(IComparer<int>)
        Span<int> sDesc = [k0, k1, k2, k3];
        sDesc.Sort(new DiffDescendingIntComparer());
        int sortDescEncoded = (sDesc[0] * 1000) + (sDesc[1] * 100) + (sDesc[2] * 10) + sDesc[3];

        // 5. Span<int>.Sort(Comparison<int> method group)
        Span<int> sMg = [k0, k1, k2, k3];
        sMg.Sort(CompareAscendingStrict);
        int sortMgEncoded = (sMg[0] * 1000) + (sMg[1] * 100) + (sMg[2] * 10) + sMg[3];

        // 6. Keys + Items Sort (default, IComparer<T>, Comparison<T> lambda)
        Span<int> kvKeys1 = [k0, k1, k2, k3];
        Span<int> kvItems1 = [1, 2, 3, 4];
        MemoryExtensions.Sort(kvKeys1, kvItems1);
        int kvDefaultItems = (kvItems1[0] * 1000) + (kvItems1[1] * 100) + (kvItems1[2] * 10) + kvItems1[3];

        Span<int> kvKeys2 = [k0, k1, k2, k3];
        Span<int> kvItems2 = [1, 2, 3, 4];
        kvKeys2.Sort(kvItems2, new DiffDescendingIntComparer());
        int kvComparerItems = (kvItems2[0] * 1000) + (kvItems2[1] * 100) + (kvItems2[2] * 10) + kvItems2[3];

        Span<int> kvKeys3 = [k0, k1, k2, k3];
        Span<int> kvItems3 = [1, 2, 3, 4];
        kvKeys3.Sort(kvItems3, (a, b) => a < b ? -1 : (a > b ? 1 : 0));
        int kvLambdaItems = (kvItems3[0] * 1000) + (kvItems3[1] * 100) + (kvItems3[2] * 10) + kvItems3[3];

        // 7. [InlineArray(4)] and unsafe fixed buffer
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

        // 8. Collection expression with element expressions and empty collection expression
        ReadOnlySpan<int> emptySlice = [];
        Span<int> spreadSpan = [100 + emptySlice.Length, inlineBuf[1], inlineBuf[2], 200];
        int spreadEncoded = spreadSpan[0] + (spreadSpan[1] * 10) + (spreadSpan[2] * 100) + (spreadSpan[3] * 1000);

        // 9. Named arguments out of parameter order (left-to-right evaluation order preserved)
        int argTrace = 0;
        int namedVal = EvalNamedOrder(
            c: StepOrder(ref argTrace, 1, x & 7),
            a: StepOrder(ref argTrace, 2, (y & 7) + 1),
            b: StepOrder(ref argTrace, 3, 5));

        // 10. Optional arguments + ref return lvalue assignment
        int opt1 = EvalOptionalDefaults(x);
        int opt2 = EvalOptionalDefaults(x, scale: 5);
        int opt3 = EvalOptionalDefaults(x, bias: 3, scale: 2);
        SelectElementByRef(spreadSpan, (x & 1) != 0) += 77;
        int refResult = spreadSpan[0] + spreadSpan[1];

        r[o + 0] = sortIntEncoded;
        r[o + 1] = sortEnumEncoded;
        r[o + 2] = sortComparablePayloads;
        r[o + 3] = sortDescEncoded;
        r[o + 4] = sortMgEncoded;
        r[o + 5] = kvDefaultItems;
        r[o + 6] = kvComparerItems;
        r[o + 7] = kvLambdaItems;
        r[o + 8] = inlineSum + fixedSum;
        r[o + 9] = spreadEncoded;
        r[o + 10] = (namedVal * 1000) + argTrace;
        r[o + 11] = opt1 + opt2 + opt3 + refResult;
    }
}
