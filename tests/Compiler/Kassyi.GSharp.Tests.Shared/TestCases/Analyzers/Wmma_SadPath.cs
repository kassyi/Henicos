using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public struct StructWithWmmaFragment
{
    public WmmaAccumulator<WmmaM16N16K16, int> Acc;
}

public static partial class WmmaSadPathKernels
{
    [GpuDeviceFunction]
    private static void LoadHelper(
        ref WmmaMatrixA<WmmaM16N16K16, sbyte, WmmaRowMajor> fragA,
        in sbyte src)
    {
        Wmma.Load(ref fragA, in src, 16);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void ValidWmmaKernel(sbyte[] a, sbyte[] b, int[] d)
    {
        var smem = new SharedMemory<sbyte>(256);
        WmmaMatrixA<WmmaM16N16K16, sbyte, WmmaRowMajor> fragA = default;
        WmmaMatrixB<WmmaM16N16K16, sbyte, WmmaColumnMajor> fragB = default;
        WmmaAccumulator<WmmaM16N16K16, int> acc = default;

        Wmma.Fill(ref acc, 0);
        LoadHelper(ref fragA, in a[0]);
        Wmma.Load(ref fragB, in smem[0], 16);
        Wmma.MultiplyAccumulate(ref acc, in fragA, in fragB, in acc);
        Wmma.Store(ref d[0], in acc, 16, WmmaMemoryLayout.RowMajor);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void InvalidFragmentAsKernelParam(WmmaAccumulator<WmmaM16N16K16, int> acc)
    {
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Sad Path")]
    public static void InvalidWmmaOperationsKernel(sbyte[] a, sbyte[] b, int[] d)
    {
        WmmaMatrixA<WmmaM8N8K4, sbyte, WmmaRowMajor> badMatA = default;
        WmmaAccumulator<WmmaM16N16K16, double> badAcc = default;

        WmmaMatrixA<WmmaM16N16K16, sbyte, WmmaRowMajor> fragA = default;
        WmmaMatrixB<WmmaM16N16K16, sbyte, WmmaRowMajor> fragB = default;
        Wmma.MultiplyAccumulate(ref badAcc, in fragA, in fragB, in badAcc);

        StructWithWmmaFragment badStruct = default;
        WmmaAccumulator<WmmaM16N16K16, int>[]? badArray = null;
        WmmaM16N16K16 badMarker = default;

        sbyte localScalar = a[0];
        Wmma.Load(ref fragA, in localScalar, 16);
        Wmma.Load(ref fragA, (sbyte)(a[0] + 1), 16);
    }
}
