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
}
