#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for a variable passed to an <c>in</c> parameter of a device function that reaches a barrier,
/// without the <c>in</c> keyword: C# passes the variable itself, so the callee reads through the parameter what it
/// wrote through another reference before the barrier (#1585).
/// </summary>
public static unsafe partial class InArgumentAliasDifferentialKernels
{
    public static readonly string[] Columns =
    [
        "inline_element", "inline_element_keyword", "inline_element_parenthesized", "parenthesized_local",
        "fixed_element", "inline_element_of_value"
    ];

    [GpuDeviceFunction]
    public static int ReadInlineAfterWrite(in int x, ref DiffInlineInt4 b)
    {
        int before = x;
        b[0] = before + 7;
        Gpu.SyncThreads();
        return x;
    }

    [GpuDeviceFunction]
    public static int ReadLocalAfterWrite(in int x, ref int y)
    {
        int before = x;
        y = before + 7;
        Gpu.SyncThreads();
        return x;
    }

    [GpuDeviceFunction]
    public static int ReadFixedAfterWrite(in int x, ref DiffFixedInt4 f)
    {
        int before = x;
        f.Buf[0] = before + 7;
        Gpu.SyncThreads();
        return x;
    }

    [GpuDeviceFunction]
    public static DiffInlineInt4 MakeInline(int value)
    {
        DiffInlineInt4 b = default;
        b[0] = value;
        return b;
    }

    [GpuKernel]
    public static void InArgumentAliasKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0x3FF;
        int o = tid * 6;

        DiffInlineInt4 buf = default;
        buf[0] = x;
        r[o + 0] = ReadInlineAfterWrite(buf[0], ref buf);

        buf[0] = x;
        r[o + 1] = ReadInlineAfterWrite(in buf[0], ref buf);

        buf[0] = x;
        r[o + 2] = ReadInlineAfterWrite((buf[0]), ref buf);

        int v = x;
        r[o + 3] = ReadLocalAfterWrite((v), ref v);

        DiffFixedInt4 fixedBuf = default;
        fixedBuf.Buf[0] = x;
        r[o + 4] = ReadFixedAfterWrite(fixedBuf.Buf[0], ref fixedBuf);

        // An element of a value is a value: the argument is a copy, and the callee reads the old value
        buf[0] = x;
        r[o + 5] = ReadInlineAfterWrite(MakeInline(x)[0], ref buf);
    }
}
