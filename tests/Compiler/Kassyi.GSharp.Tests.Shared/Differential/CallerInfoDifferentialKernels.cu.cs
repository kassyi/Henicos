#nullable enable
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.DeviceLibrary;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for caller-info parameters (<c>[CallerLineNumber]</c>) that a call in GPU code omits: each
/// receives the line of the call site, as C# passes it, on every path (#946).
/// </summary>
/// <remarks>
/// The line numbers are part of what the tests check: the oracle runs these kernels as C#, so moving a call to another
/// line changes the expected value and the paths together.
/// </remarks>
public static partial class CallerInfoDifferentialKernels
{
    public static readonly string[] Columns =
    [
        "plain", "paren_on_next_line", "nested", "long", "constructor", "explicit", "mapped", "named_after",
        "extension", "library", "library_extension", "local_function", "lambda",
    ];

    [GpuDeviceFunction]
    public static int Line([CallerLineNumber] int line = 0) => line;

    [GpuDeviceFunction]
    public static int LineOfInner() => Line();

    [GpuDeviceFunction]
    public static long WideLine([CallerLineNumber] long line = 0) => line;

    [GpuDeviceFunction]
    public static int Offset(int x, [CallerLineNumber] int line = 0, int bias = 0) => (x * 1000) + line + bias;

    // A float Sum is summed in order on the managed path, so this function gets a managed version there
    [GpuDeviceFunction]
    public static int LineWithSum(float[] data, [CallerLineNumber] int line = 0) =>
        line + (int)(data.AsStackEnumerable().Sum() * 0f);

    [GpuDeviceFunction]
    public static int LineAcrossBarrier(int x, [CallerLineNumber] int line = 0)
    {
        Gpu.SyncThreads();
        return (x * 1000) + line;
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Lines(int count, [CallerLineNumber] int line = 0)
    {
        for (int i = 0; i < count; i++)
        {
            yield return line + i;
        }
    }

    [GpuKernel]
    public static void CallerLineKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int o = tid * 13;
        r[o + 0] = Line();
        r[o + 1] = Line
        (
        );
        r[o + 2] = LineOfInner();
        r[o + 3] = (int)WideLine();
        r[o + 4] = new CallerInfoMarked(x).Line;
        r[o + 5] = Line(x);
#line 9000
        r[o + 6] = Line();
#line default
        r[o + 7] = Offset(x, bias: 3);
        r[o + 8] = x.LineAfter();
        r[o + 9] = LibLanguageExports.CallerLine();
        r[o + 10] = x.LibLineAfter();
        r[o + 11] = LocalLine();
        r[o + 12] = xs.AsStackEnumerable().Take(1).Select(static v => Line()).Sum();

        static int LocalLine() => Line();
    }

    // The managed path calls the managed version of LineWithSum; C# cannot run the kernel as written
    [GpuKernel]
    public static void CallerLineManagedVersionKernel(float[] data, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= r.Length) return;
        r[tid] = LineWithSum(data);
    }

    // A device function that reaches a barrier is called through the resume protocol
    [GpuKernel]
    public static void CallerLineSuspendingCallKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0x3FF;
        r[tid] = LineAcrossBarrier(x);
    }

    // An iterator device function is inlined into the loop that reads it
    [GpuKernel]
    public static void CallerLineIteratorKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int sum = xs[tid] & 0x3FF;
        foreach (int line in Lines(2))
        {
            sum += line;
        }

        r[tid] = sum;
    }
}

public static partial class CallerInfoExtensions
{
    [GpuDeviceFunction]
    public static int LineAfter(this int x, [CallerLineNumber] int line = 0) => (x * 1000) + line;
}

public struct CallerInfoMarked
{
    public int Value;
    public int Line;

    public CallerInfoMarked(int value, [CallerLineNumber] int line = 0)
    {
        Value = value;
        Line = line;
    }
}
