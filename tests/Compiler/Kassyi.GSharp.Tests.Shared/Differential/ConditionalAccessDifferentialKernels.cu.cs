#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for the null-conditional access <c>?.</c> on an array: the length and an element of a null and of
/// a non-null array, as a value and combined with <c>??</c>.
/// </summary>
public struct ConditionalPoint
{
    public int X;
    public int Y;
}

public struct ConditionalHolder
{
    public int[]? Data;
}

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
}
