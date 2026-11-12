#nullable enable
#pragma warning disable CS8600, CS8601, CS8602, CS8604 // Nullable arrays flow into non-nullable places on purpose
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

public struct NullCheckHolder
{
    public int[]? Data;
}

// A non-nullable array field, which the launchers check, so the kernel reads it without a check (#1409)
public struct ProvenHolder
{
    public int[] Data;
}

// Which array reads get the null check (__gsharp_array_ref, #706): a receiver that may be null does, a kernel array
// parameter included; a stack array local and a span do not
[SkipSnapshotConsistency("Array null checks checked by EmissionShapeTests")]
public static partial class ArrayNullCheckShapeKernels
{
    [GpuKernel]
    public static void ElidedKernel(int[] values, int[]? bias, int[] o)
    {
        int[] local = new int[4];
        local[0] = values.Length;
        o[0] = values[0] + local.Length + local[0];
        o[1] = bias.Length;
        foreach (var v in values)
        {
            o[2] += v;
        }

        o[3] = (o[0] > 0 ? values : o).Length;
    }

    [GpuKernel]
    public static void CheckedKernel(NullCheckHolder h, int[] written, int[] o, int[] tupled, int[] outWritten,
        int[] fnWritten)
    {
        written = h.Data;
        int[] copy = h.Data;
        o[0] = h.Data.Length;
        o[1] = written[0];
        o[2] = copy.Length;
        o[3] = Sum(copy);
        System.ReadOnlySpan<int> span = h.Data;
        o[4] = span.Length + (h.Data is null ? 1 : 0);
        o[5] = (o[0] > 0 ? o : null).Length;

        // Writes of a parameter in a tuple, through out and inside a local function
        int x;
        (tupled, x) = (h.Data, 1);
        Take(h.Data, out outWritten);
        Reset();
        o[6] = tupled.Length + outWritten.Length + fnWritten.Length + x;

        void Reset() => fnWritten = h.Data;
    }

    // A non-nullable field of a struct argument is read unchecked; a call checks what it passes to a non-nullable
    // parameter (h.Data may be null; p.Data and the struct p are proven) and the callee reads its parameter unchecked
    [GpuKernel]
    public static void BoundaryKernel(ProvenHolder p, NullCheckHolder h, int[] o)
    {
        o[0] = p.Data[0];
        o[1] = ReadFirst(h.Data);
        o[2] = ReadFirst(p.Data);
        var made = new ProvenHolder { Data = h.Data };
        o[3] = ReadHolder(made);
        o[4] = ReadHolder(p);
    }

    [GpuDeviceFunction]
    private static int ReadFirst(int[] a) => a[0];

    [GpuDeviceFunction]
    private static int ReadHolder(ProvenHolder holder) => holder.Data.Length;

    [GpuDeviceFunction]
    private static void Take(int[]? source, out int[]? target)
    {
        target = source;
    }

    [GpuDeviceFunction]
    private static int Sum(int[]? a)
    {
        int s = 0;
        foreach (var v in a)
        {
            s += v;
        }

        return s;
    }

    // Gpu.NotNull checks its array once; the local it initializes is proven, so its reads and the call that passes it
    // are not checked, and Gpu.NotNull of an array that is already proven checks nothing
    [GpuKernel]
    public static void NotNullKernel(NullCheckHolder h, int[] values, int[] o)
    {
        int[] data = Gpu.NotNull(h.Data);
        o[0] = data.Length + data[0] + ReadFirst(data);
        o[1] = Gpu.NotNull(values).Length;
    }
}
