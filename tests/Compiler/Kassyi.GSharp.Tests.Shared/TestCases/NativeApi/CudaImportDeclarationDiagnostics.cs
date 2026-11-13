using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace DeclarationDiagnostics;

public static partial class Imports
{
    [CudaImport("m.fatbin")]
    public static extern void External(float[] x, uint gridDimX, uint blockDimX);

    [CudaImport("m.fatbin")]
    public static void Bodied(float[] x) { }

    [CudaImport("m.fatbin")]
    public static partial void Generic<T>(T[] x) where T : unmanaged;

    [CudaImport("m.fatbin")]
    public static partial int Counted(float[] x);

    [CudaImport("m.fatbin")]
    public static partial void Implemented(float[] x);

    public static partial void Implemented(float[] x) { }

    [CudaImport("m.fatbin")]
    public static partial void Passable(float[] x, uint gridDimX);
}

public static class NotPartial
{
    [CudaImport("m.fatbin")]
    public static partial void Run(float[] x);
}

public static class Outer
{
    public static partial class Inner
    {
        [CudaImport("m.fatbin")]
        public static partial void Run(float[] x);
    }
}

file static partial class FileLocal
{
    [CudaImport("m.fatbin")]
    public static partial void Run(float[] x);
}
