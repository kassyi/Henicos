using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A struct with a field, as an element of an array.</summary>
public struct SplitOrderCell
{
    public int F;
}

/// <summary>
/// Kernels whose statements evaluate operands before a split point (a warp shuffle or a device function that reaches a
/// barrier): C# evaluates them in the order written, so an operand left of the split sees nothing the split or the
/// operands after it do. Every lane works on its own elements, every shuffle reads the calling lane and every barrier
/// is reached by every thread of the block, so the C# of each lane alone defines the result.
/// </summary>
public static partial class SplitOperandOrderDifferentialKernels
{
    public const uint Full = 0xFFFFFFFFu;

    public static readonly string[] OrderColumns =
    [
        "left_assigned", "left_call", "left_read_barrier", "two_earlier", "nested_left_arg", "out_declaration",
        "conditional_left_arg", "conditional_left_count", "later_declarator_first", "later_declarator",
        "later_declarator_shuffle", "expression_bodied", "index_stays", "check_index_count", "check_index_value",
        "mixed_loop"
    ];

    public static readonly string[] CompoundColumns =
    [
        "element_before_shuffle", "local_before_shuffle", "then_read_same_element", "nested_outer", "nested_inner",
        "int_add", "long_add", "shift_left", "unsigned_shift_right", "float_remainder", "byte_narrowing",
        "int_subtract", "bool_and"
    ];

    [GpuDeviceFunction]
    public static int SyncThen(int v)
    {
        Gpu.SyncThreads();
        return v;
    }

    [GpuDeviceFunction]
    public static int SyncSum(int a, int b)
    {
        Gpu.SyncThreads();
        return a + b;
    }

    [GpuDeviceFunction]
    public static int SyncThenBump(int[] data, int i)
    {
        Gpu.SyncThreads();
        data[i] += 7;
        return data[i];
    }

    [GpuDeviceFunction]
    public static int SyncRead(int[] data, int i)
    {
        Gpu.SyncThreads();
        return data[i];
    }

    [GpuDeviceFunction]
    public static int Bump(int[] data, int i)
    {
        data[i] += 7;
        return data[i];
    }

    [GpuDeviceFunction]
    public static int Add3(int[] data, int i)
    {
        data[i] += 3;
        return data[i];
    }

    [GpuDeviceFunction]
    public static int Double(int[] data, int i)
    {
        data[i] *= 2;
        return data[i];
    }

    [GpuDeviceFunction]
    public static int Triple(int a, int b, int c) => a * 100 + b * 10 + c;

    [GpuDeviceFunction]
    public static int SetTenReturnOne(ref int x)
    {
        x = 10;
        return 1;
    }

    [GpuDeviceFunction]
    public static int SetOut(out int y, int l)
    {
        y = l * 3;
        return 1;
    }

    [GpuDeviceFunction]
    public static int Mark(int[] data, int i)
    {
        data[i] = i + 1;
        return i;
    }

    [GpuDeviceFunction]
    public static int NextIndex(int[] counter, int l)
    {
        counter[l]++;
        return l;
    }

    // The value of Bump, then a split that reads the element again
    [GpuDeviceFunction]
    public static int BumpPlusSync(int[] data, int i) => Bump(data, i) + SyncThen(data[i]);

    [GpuKernel]
    public static void OperandOrderKernel(int[] a, int[] b, int[] c, int[] d, int[] e, int[] f, int[] g, int[] h,
        int[] counter, SplitOrderCell[] cells, bool flag, int[] output)
    {
        int l = ThreadIndex.X;
        int o = l * 16;
        output[o + 0] = (a[l] = l + 100) + Warp.ShuffleSync(Full, a[l], l);
        output[o + 1] = Bump(b, l) + Warp.ShuffleSync(Full, b[l], l);
        output[o + 2] = c[l] + SyncThenBump(c, l);
        output[o + 3] = Triple(Add3(d, l), Double(d, l), Warp.ShuffleSync(Full, d[l], l));
        int x = l;
        output[o + 4] = SyncSum(0, x + SetTenReturnOne(ref x));
        output[o + 5] = SyncSum(SetOut(out int y, l), y);
        output[o + 6] = SyncSum(Bump(e, l), flag ? SyncThen(e[l]) : -1);
        output[o + 7] = e[l];
        int m = Mark(f, l), q = flag ? SyncRead(f, l) : -1;
        output[o + 8] = m;
        output[o + 9] = q;
        int p = Bump(g, l), r = p + Warp.ShuffleSync(Full, g[l], l);
        output[o + 10] = r;
        output[o + 11] = BumpPlusSync(h, l);
        output[o + 12] = Warp.ShuffleSync(Full, l * 5, l);
        cells[NextIndex(counter, l)].F = SyncThen(l + 1);
        output[o + 13] = counter[l];
        output[o + 14] = cells[l].F;
        int sum = 0;
        for (int k = 0; k < 2; k++)
        {
            sum += k + Warp.ShuffleSync(Full, sum, l);
        }

        output[o + 15] = sum + SyncSum(sum, Bump(a, l));
    }

    [GpuDeviceFunction]
    public static int SetTo(ref int x, int v)
    {
        x = v;
        return 2;
    }

    [GpuDeviceFunction]
    public static long SetToLong(ref long x, long v)
    {
        x = v;
        return 2L;
    }

    [GpuDeviceFunction]
    public static float SetToFloat(ref float x, float v)
    {
        x = v;
        return 2.5f;
    }

    [GpuDeviceFunction]
    public static bool SetToBool(ref bool x, bool v)
    {
        x = v;
        return true;
    }

    [GpuDeviceFunction]
    public static int SetToByte(ref byte x, byte v)
    {
        x = v;
        return 10;
    }

    [GpuDeviceFunction]
    public static long SyncThenLong(long v)
    {
        Gpu.SyncThreads();
        return v;
    }

    [GpuDeviceFunction]
    public static float SyncThenFloat(float v)
    {
        Gpu.SyncThreads();
        return v;
    }

    [GpuDeviceFunction]
    public static bool SyncThenBool(bool v)
    {
        Gpu.SyncThreads();
        return v;
    }

    [GpuDeviceFunction]
    public static int BumpBoth(int[] a, int[] b, int i)
    {
        a[i] += 7;
        b[i] += 5;
        return 1;
    }

    [GpuKernel]
    public static void CompoundOrderKernel(int[] a, int[] b, int[] c, int[] d, int[] e, int x, int y, int[] output)
    {
        int l = ThreadIndex.X;
        int o = l * 13;
        a[l] += Warp.ShuffleSync(Full, Bump(a, l), l);
        output[o + 0] = a[l];
        int local = l;
        local += Warp.ShuffleSync(Full, SetTo(ref local, 50), l);
        output[o + 1] = local;
        output[o + 2] = Triple(b[l] += Warp.ShuffleSync(Full, x, l), b[l], Warp.ShuffleSync(Full, y, l));
        c[l] += d[l] += Warp.ShuffleSync(Full, BumpBoth(c, d, l), l);
        output[o + 3] = c[l];
        output[o + 4] = d[l];
        int i32 = l + 1;
        i32 += SyncThen(SetTo(ref i32, 1000));
        output[o + 5] = i32;
        long i64 = l + 1L;
        i64 += SyncThenLong(SetToLong(ref i64, 1L << 40));
        output[o + 6] = (int)(i64 ^ (i64 >> 32));
        int shifted = l + 1;
        shifted <<= SyncThen(SetTo(ref shifted, 3));
        output[o + 7] = shifted;
        int unsignedShifted = -(l + 1);
        unsignedShifted >>>= SyncThen(SetTo(ref unsignedShifted, 64));
        output[o + 8] = unsignedShifted;
        float remainder = l + 7.5f;
        remainder %= SyncThenFloat(SetToFloat(ref remainder, 1.25f));
        output[o + 9] = (int)(remainder * 16f);
        byte narrow = (byte)(250 + (l & 3));
        narrow += (byte)SyncThen(SetToByte(ref narrow, 3));
        output[o + 10] = narrow;
        // checked is not supported in kernels (GSH0003)
        int difference = l + e[l];
        difference -= SyncThen(SetTo(ref difference, 5));
        output[o + 11] = difference;
        bool both = (l & 1) == 0;
        both &= SyncThenBool(SetToBool(ref both, true));
        output[o + 12] = both ? 1 : 0;
    }

    // A split, then a side effect the caller can observe
    [GpuDeviceFunction]
    public static int SyncThenCount(int[] counts)
    {
        Gpu.SyncThreads();
        counts[0]++;
        return 1;
    }

    // The same side effect without a split
    [GpuDeviceFunction]
    public static int Count(int[] counts)
    {
        counts[0]++;
        return 1;
    }

    [GpuDeviceFunction]
    public static int SyncThenDivide(int[] counts, int zero)
    {
        Gpu.SyncThreads();
        counts[0]++;
        return 1 / zero;
    }

    [GpuDeviceFunction]
    public static int StoreValue(ref int target, int value)
    {
        target = value;
        return value;
    }

    // C# selects the span element (its indexer checks the index) before the value
    [GpuKernel]
    public static void SpanElementTargetChecksFirstKernel(int[] data, int[] counts, int i)
    {
        Span<int> s = data.AsSpan(0, 4);
        s[i] = SyncThenCount(counts);
    }

    [GpuKernel]
    public static void SpanElementTargetChecksFirstTwinKernel(int[] data, int[] counts, int i)
    {
        Span<int> s = data.AsSpan(0, 4);
        s[i] = Count(counts);
    }

    // C# selects the element whose field is assigned before the value
    [GpuKernel]
    public static void FieldOfElementTargetChecksFirstKernel(SplitOrderCell[] cells, int[] counts, int i)
    {
        cells[i].F = SyncThenCount(counts);
    }

    [GpuKernel]
    public static void FieldOfElementTargetChecksFirstTwinKernel(SplitOrderCell[] cells, int[] counts, int i)
    {
        cells[i].F = Count(counts);
    }

    // C# selects the element passed by reference before the next argument
    [GpuKernel]
    public static void RefElementArgumentChecksFirstKernel(int[] data, int[] counts, int i)
    {
        StoreValue(ref data[i], SyncThenCount(counts));
    }

    [GpuKernel]
    public static void RefElementArgumentChecksFirstTwinKernel(int[] data, int[] counts, int i)
    {
        StoreValue(ref data[i], Count(counts));
    }

    // C# checks an array element that is assigned when it stores the value, after the value
    [GpuKernel]
    public static void ElementTargetChecksAtStoreKernel(int[] data, int[] counts, int i)
    {
        data[i] = SyncThenCount(counts);
    }

    [GpuKernel]
    public static void ElementTargetChecksAtStoreTwinKernel(int[] data, int[] counts, int i)
    {
        data[i] = Count(counts);
    }

    // The index check of the field's element comes first, so the division by zero never runs
    [GpuKernel]
    public static void FieldOfElementBeforeDivisionKernel(SplitOrderCell[] cells, int[] counts, int i, int zero)
    {
        cells[i].F = SyncThenDivide(counts, zero);
    }

    // The division by zero comes first, the index of the assigned element is checked at the store
    [GpuKernel]
    public static void ElementBeforeDivisionKernel(int[] data, int[] counts, int i, int zero)
    {
        data[i] = SyncThenDivide(counts, zero);
    }
}
