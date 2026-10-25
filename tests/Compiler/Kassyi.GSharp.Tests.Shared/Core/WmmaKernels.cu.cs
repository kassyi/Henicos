using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

public partial class WmmaKernels
{
    [GpuDeviceFunction]
    private static void MultiplyAccumulateHelper(
        ref WmmaAccumulator<WmmaM16N16K16, int> acc,
        in WmmaMatrixA<WmmaM16N16K16, sbyte, WmmaRowMajor> fragA,
        in WmmaMatrixB<WmmaM16N16K16, sbyte, WmmaRowMajor> fragB)
    {
        Wmma.MultiplyAccumulate(ref acc, in fragA, in fragB, in acc);
    }

    [GpuKernel]
    public static void WmmaInt8RowMajorKernel(sbyte[] a, sbyte[] b, int[] c, int[] d)
    {
        WmmaMatrixA<WmmaM16N16K16, sbyte, WmmaRowMajor> fragA = default;
        WmmaMatrixB<WmmaM16N16K16, sbyte, WmmaRowMajor> fragB = default;
        WmmaAccumulator<WmmaM16N16K16, int> acc = default;

        Wmma.Load(ref fragA, in a[0], 16);
        Wmma.Load(ref fragB, in b[0], 16);
        Wmma.Load(ref acc, in c[0], 16, WmmaMemoryLayout.RowMajor);
        MultiplyAccumulateHelper(ref acc, in fragA, in fragB);
        Wmma.Store(ref d[0], in acc, 16, WmmaMemoryLayout.RowMajor);
    }

    [GpuKernel]
    public static void WmmaInt8ColMajorBKernel(sbyte[] a, sbyte[] bColMajor, int[] c, int[] d)
    {
        WmmaMatrixA<WmmaM16N16K16, sbyte, WmmaRowMajor> fragA = default;
        WmmaMatrixB<WmmaM16N16K16, sbyte, WmmaColumnMajor> fragB = default;
        WmmaAccumulator<WmmaM16N16K16, int> acc = default;

        Wmma.Load(ref fragA, in a[0], 16);
        Wmma.Load(ref fragB, in bColMajor[0], 16);
        Wmma.Load(ref acc, in c[0], 16, WmmaMemoryLayout.RowMajor);
        Wmma.MultiplyAccumulate(ref acc, in fragA, in fragB, in acc);
        Wmma.Store(ref d[0], in acc, 16, WmmaMemoryLayout.RowMajor);
    }

    [GpuKernel]
    public static void WmmaInt8ColMajorStoreKernel(sbyte[] a, sbyte[] b, int[] dColMajor)
    {
        WmmaMatrixA<WmmaM16N16K16, sbyte, WmmaRowMajor> fragA = default;
        WmmaMatrixB<WmmaM16N16K16, sbyte, WmmaRowMajor> fragB = default;
        WmmaAccumulator<WmmaM16N16K16, int> acc = default;

        Wmma.Fill(ref acc, 0);
        Wmma.Load(ref fragA, in a[0], 16);
        Wmma.Load(ref fragB, in b[0], 16);
        Wmma.MultiplyAccumulate(ref acc, in fragA, in fragB, in acc);
        Wmma.Store(ref dColMajor[0], in acc, 16, WmmaMemoryLayout.ColumnMajor);
    }

    [GpuKernel]
    public static void WmmaDoubleM8N8K4Kernel(double[] a, double[] b, double[] d)
    {
        WmmaMatrixA<WmmaM8N8K4, double, WmmaRowMajor> fragA = default;
        WmmaMatrixB<WmmaM8N8K4, double, WmmaRowMajor> fragB = default;
        WmmaAccumulator<WmmaM8N8K4, double> acc = default;

        Wmma.Fill(ref acc, 0.0);
        Wmma.Load(ref fragA, in a[0], 4);
        Wmma.Load(ref fragB, in b[0], 8);
        Wmma.MultiplyAccumulate(ref acc, in fragA, in fragB, in acc);
        Wmma.Store(ref d[0], in acc, 8, WmmaMemoryLayout.RowMajor);
    }
}
