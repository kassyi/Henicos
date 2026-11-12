using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace TestNamespace;

public static unsafe partial class ImportedKernels
{
    [CudaImport("k.fatbin")]
    public static partial void Passable([In] float[] input, float factor, int* counts, ushort gridDimX, nint stream);

    [CudaImport("k.fatbin")]
    public static partial void Text(string text);

    [CudaImport("k.fatbin")]
    public static partial void Callback(System.Action callback);

    [CudaImport("k.fatbin")]
    public static partial void Rows([In] float[][] rows);

    [CudaImport("k.fatbin")]
    public static partial void Streamed(float[] data, int stream);

    [CudaImport("k.fatbin")]
    public static partial void Optional(int? count);

    [CudaImport("k.fatbin")]
    public static partial void Counted(ref int total, float[,] grid);

    [CudaImport("k.fatbin")]
    public static partial void Paired((byte, double) pair, AutoPair auto, HoldsAuto holder, DateTime time);

    [CudaImport("k.fatbin")]
    public static partial void AutoElements(AutoPair[] items);

    [CudaImport("k.fatbin")]
    public static partial void Spans(scoped Span<float> values);

    [CudaImport("k.fatbin")]
    public static partial void Written(out int result, in int seed, ref readonly int limit);

    [CudaImport("k.fatbin")]
    public static partial void Gridded(float[] data, Grid gridDimX, Grid blockDimX);

    [CudaImport("k.fatbin")]
    public static partial void Called(delegate* unmanaged<int, void> callback, delegate*<ref int, float> compute);

    [CudaImport("k.fatbin")]
    public static partial void Many(int __gsharp_d_count, params float[] values);
}

[StructLayout(LayoutKind.Auto)]
public struct AutoPair
{
    public byte A;
    public double B;
}

public struct HoldsAuto
{
    public int Before;
    public AutoPair Inner;
}

// A dimension that converts to uint through a user-defined operator, which the launch applies as any C# call does
public struct Grid
{
    public uint Value;

    public static implicit operator uint(Grid grid) => grid.Value;
}
