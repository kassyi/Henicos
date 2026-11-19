#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A struct read through a <c>Nullable&lt;T&gt;</c> by the null-conditional access.</summary>
public struct ConditionalPoint
{
    public int X;
    public int Y;
}

/// <summary>A struct whose array field is read by a chained null-conditional access.</summary>
public struct ConditionalHolder
{
    public int[]? Data;
}

/// <summary>
/// A struct with a computed property and a nested struct field, read through a <c>Nullable&lt;T&gt;</c>.
/// </summary>
public struct ConditionalComputed
{
    public int X;

    public ConditionalPoint Inner;

    public int Twice => X * 2;
}

/// <summary>
/// Differential kernels for the null-conditional access <c>?.</c> and <c>?[]</c> on an array and on a
/// <c>Nullable&lt;T&gt;</c> of a struct: the length and an element of a null and of a non-null receiver, as a value,
/// combined with <c>??</c>, chained, assigned through, next to and across a barrier.
/// </summary>
public static partial class ConditionalAccessDifferentialKernels
{
    [GpuKernel]
    public static void ArrayConditionalAccessKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int[]? picked = a[t] > 0 ? null : a;
        int? length = picked?.Length;
        int? element = picked?[1];
        int fallback = picked?.Length ?? -1;

        int col = t * 5;
        dst[col + 0] = length.HasValue ? 1 : 0;
        dst[col + 1] = length ?? -7;
        dst[col + 2] = element.HasValue ? 1 : 0;
        dst[col + 3] = element ?? -9;
        dst[col + 4] = fallback;
    }

    [GpuKernel]
    public static void NullableStructConditionalAccessKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        ConditionalPoint? p = a[t] > 0 ? null : new ConditionalPoint { X = a[t], Y = 3 };
        int? x = p?.X;
        int? y = p?.Y;
        int sum = (p?.X ?? 100) + (p?.Y ?? 1000);

        int col = t * 5;
        dst[col + 0] = x.HasValue ? 1 : 0;
        dst[col + 1] = x ?? -5;
        dst[col + 2] = y.HasValue ? 1 : 0;
        dst[col + 3] = y ?? -6;
        dst[col + 4] = sum;
    }

    [GpuKernel]
    public static void ChainedConditionalAccessKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        // The holder is null for a quarter of the rows, its array for another quarter
        int sel = a[t] & 3;
        ConditionalHolder? holder = sel == 0 ? null : new ConditionalHolder { Data = sel == 1 ? null : a };
        int? length = holder?.Data?.Length;
        int? first = holder?.Data?[0];
        int viaCoalesce = holder?.Data?.Length ?? -3;

        int col = t * 5;
        dst[col + 0] = length.HasValue ? 1 : 0;
        dst[col + 1] = length ?? -4;
        dst[col + 2] = first.HasValue ? 1 : 0;
        dst[col + 3] = first ?? -8;
        dst[col + 4] = viaCoalesce;
    }

    [GpuDeviceFunction]
    public static ConditionalPoint? PickPoint(int selector, ref int calls)
    {
        calls++;
        return selector > 0 ? null : new ConditionalPoint { X = selector, Y = 4 };
    }

    [GpuKernel]
    public static void ReceiverEvaluatedOnceKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int calls = 0;
        int? x = PickPoint(a[t], ref calls)?.X;
        int? y = PickPoint(a[t], ref calls)?.Y;

        int col = t * 3;
        dst[col + 0] = calls;
        dst[col + 1] = x ?? -2;
        dst[col + 2] = y ?? -3;
    }

    [GpuDeviceFunction]
    public static int CountEvaluation(ref int evaluations)
    {
        evaluations++;
        return 42;
    }

    [GpuKernel]
    public static void ConditionalAssignmentKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int col = t * 4;
        dst[col + 0] = -1;
        dst[col + 3] = -1;
        int[]? target = a[t] > 0 ? null : dst;
        int evaluations = 0;

        // The right side is evaluated only when the target is not null
        target?[col] = CountEvaluation(ref evaluations);
        int? assigned = (target?[col + 3] = 7);

        dst[col + 1] = evaluations;
        dst[col + 2] = assigned ?? -9;
    }

    [GpuKernel]
    public static void StackArrayConditionalAccessKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        // A stack array is never null: the access always has a value
        int[] local = new int[4];
        local[2] = a[t];
        int? length = local?.Length;
        int? element = local?[2];

        int col = t * 3;
        dst[col + 0] = length ?? -1;
        dst[col + 1] = element ?? -2;
        dst[col + 2] = (local?[2] ?? 0) + 1;
    }

    [GpuDeviceFunction]
    public static int PlainEvaluation(ref int evaluations)
    {
        evaluations++;
        return 1;
    }

    [GpuDeviceFunction]
    public static int BarrierEvaluation(ref int evaluations)
    {
        Gpu.SyncThreads();
        evaluations++;
        return 1;
    }

    [GpuDeviceFunction]
    public static ConditionalPoint? BarrierPickPoint(int selector, ref int evaluations)
    {
        Gpu.SyncThreads();
        evaluations++;
        return selector == 0 ? null : new ConditionalPoint { X = 7, Y = 4 };
    }

    // The receiver itself suspends: it runs before the null test, for every row
    [GpuKernel]
    public static void SuspendingReceiverKernel(int[] a, int[] dst)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int e = 0;
        int? x = BarrierPickPoint(a[t] & 1, ref e)?.X;

        int col = t * 3;
        dst[col + 0] = e;
        dst[col + 1] = x.HasValue ? 1 : 0;
        dst[col + 2] = x ?? -1;
    }

    // The index is evaluated only for a non-null receiver, and a barrier follows the access
    [GpuKernel]
    public static void AccessBeforeBarrierKernel(int[] a, int[] dst)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int[]? src = (a[t] & 1) == 0 ? null : a;
        int e = 0;
        int? v = src?[PlainEvaluation(ref e)];
        Gpu.SyncThreads();

        int col = t * 3;
        dst[col + 0] = e;
        dst[col + 1] = v.HasValue ? 1 : 0;
        dst[col + 2] = v ?? -1;
    }

    // An access in a loop that holds a barrier: the loop variable and the ref argument live in the frame
    [GpuKernel]
    public static void AccessInBarrierLoopKernel(int[] a, int[] dst)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int[]? src = (a[t] & 1) == 0 ? null : a;
        int sum = 0;
        int e = 0;
        for (int i = 0; i < 2; i++)
        {
            int? v = src?[i + PlainEvaluation(ref e) - 1];
            Gpu.SyncThreads();
            sum += v ?? -1;
        }

        int col = t * 2;
        dst[col + 0] = e;
        dst[col + 1] = sum;
    }

    // A suspending call as an operand next to the access, after it and before it
    [GpuKernel]
    public static void SuspendingCallBesideAccessKernel(int[] a, int[] dst)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int[]? src = (a[t] & 1) == 0 ? null : a;
        int e = 0;
        int g = 0;
        int r1 = (src?[PlainEvaluation(ref e)] ?? -1) + BarrierEvaluation(ref g);
        int r2 = BarrierEvaluation(ref g) + (src?[PlainEvaluation(ref e)] ?? -1);

        int col = t * 4;
        dst[col + 0] = e;
        dst[col + 1] = g;
        dst[col + 2] = r1;
        dst[col + 3] = r2;
    }

    // A compound assignment through ?[]: the right side runs only for a non-null receiver
    [GpuKernel]
    public static void CompoundAssignmentKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int k = t * 2;
        dst[k] = 5;
        int[]? target = (a[t] & 1) == 0 ? null : dst;
        int e = 0;
        target?[k] += PlainEvaluation(ref e) + 1;
        dst[k + 1] = e;
    }

    [GpuDeviceFunction]
    public static int Bump(int[] target, int index)
    {
        target[index] += 10;
        return 1;
    }

    [GpuDeviceFunction]
    public static int Combine(int x, int y) => x * 100 + y;

    // A read through ?[] and a write of the same element by a call, in both orders
    [GpuKernel]
    public static void ReadAndWriteOrderKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int k = t * 3;
        dst[k] = 5;
        int[]? src = (a[t] & 1) == 0 ? null : dst;
        int r1 = (src?[k] ?? -1) + Bump(dst, k);
        int r2 = Combine(Bump(dst, k), src?[k] ?? -1);
        dst[k + 1] = r1;
        dst[k + 2] = r2;
    }

    // A suspending call in one arm of ?: and the access in the other; mode is the same for every thread
    [GpuKernel]
    public static void SuspendingCallInOtherBranchKernel(int[] a, int[] dst, int mode)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int[]? src = (a[t] & 1) == 0 ? null : a;
        int e = 0;
        int g = 0;
        int v = mode == 2 ? BarrierEvaluation(ref g) : (src?[PlainEvaluation(ref e)] ?? -1);

        int col = t * 3;
        dst[col + 0] = g;
        dst[col + 1] = e;
        dst[col + 2] = v;
    }

    // A computed property and a nested field of a Nullable<S> receiver
    [GpuKernel]
    public static void ComputedMemberKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        ConditionalComputed? p = (a[t] & 1) == 0
            ? null
            : new ConditionalComputed { X = 5, Inner = new ConditionalPoint { X = 1, Y = 9 } };
        int? twice = p?.Twice;
        int? inner = p?.Inner.Y;

        int col = t * 2;
        dst[col + 0] = twice ?? -1;
        dst[col + 1] = inner ?? -1;
    }

    // The array field of a struct parameter, as in the report of the missing diagnostic
    [GpuKernel]
    public static void HolderParameterKernel(ConditionalHolder h, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        dst[t] = h.Data?.Length ?? -1;
    }

    // An extension method called through ?.
    [GpuKernel]
    public static void ExtensionCallKernel(int[] a, int[] dst, int n)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= n)
        {
            return;
        }

        int[]? src = (a[t] & 1) == 0 ? null : a;
        int? s = src?.SumOfFirstTwo();

        int col = t * 2;
        dst[col + 0] = s.HasValue ? 1 : 0;
        dst[col + 1] = s ?? -1;
    }
}

// Internal, so that the generator snapshot, which compiles this file next to the test assembly, sees one SumOfFirstTwo
internal static partial class ConditionalAccessExtensions
{
    [GpuDeviceFunction]
    public static int SumOfFirstTwo(this int[] values) => values[0] + values[1];
}
