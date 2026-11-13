// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels that test the differential harness itself (<see cref="DifferentialRunnerTests"/>): ones whose result every
/// path must reproduce, and one whose result depends on the thread order, which the oracle's order check must reject.
/// </summary>
public static partial class HarnessProbeKernels
{
    /// <summary>The columns of <see cref="UInt32AddMatrixKernel"/>.</summary>
    public static readonly string[] UInt32AddColumns = ["a+b", "a^b"];

    // Unsigned arithmetic wraps in C# and C++ alike, so the expected result does not depend on a pending fix (#722)
    [GpuKernel]
    public static void UInt32AddMatrixKernel(uint[] a, uint[] b, uint[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t >= a.Length)
        {
            return;
        }

        int o = t * 2;
        r[o] = a[t] + b[t];
        r[o + 1] = a[t] ^ b[t];
    }

    // Every thread writes its own id to the same cell: the thread that runs last decides the value (a data race)
    [GpuKernel]
    public static void LastWriterKernel(int[] r)
    {
        r[0] = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
    }

    // The oracle of the harness tests' fake case: r[t * 2] = a + b (wrapping) and r[t * 2 + 1] = a ^ b, for the linear
    // id t of a two-dimensional launch
    [GpuKernel]
    public static void Int32AddXorKernel(int[] a, int[] b, int[] r)
    {
        int blockId = BlockIndex.X + BlockIndex.Y * GridDimension.X;
        int threadInBlock = ThreadIndex.X + ThreadIndex.Y * BlockDimension.X;
        int t = blockId * BlockDimension.X * BlockDimension.Y + threadInBlock;
        if (t >= a.Length)
        {
            return;
        }

        r[t * 2] = (int)((uint)a[t] + (uint)b[t]);
        r[t * 2 + 1] = a[t] ^ b[t];
    }

    // r[t] = idx[idx[t]]: an index outside idx throws IndexOutOfRangeException
    [GpuKernel]
    public static void DoubleIndexKernel(int[] idx, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t < r.Length)
        {
            r[t] = idx[idx[t]];
        }
    }

    // Every thread writes r[0] = t + 10 before it reads idx[idx[t]]: with idx[0] = -1 thread 0 throws, and r[0] holds
    // the value of the thread that ran last, so it depends on the thread order
    [GpuKernel]
    public static void RacingWriteThenThrowKernel(int[] idx, int[] r)
    {
        int t = ThreadIndex.X;
        r[0] = t + 10;
        r[1] = idx[idx[t]];
    }

    // r[t] = idx[idx[t]] / idx[t]: with idx = {-1, 1, 2, 0} thread 0 throws IndexOutOfRangeException and thread 3
    // DivideByZeroException, so the first exception depends on the thread order
    [GpuKernel]
    public static void TwoExceptionTypesKernel(int[] idx, int[] r)
    {
        int t = ThreadIndex.X;
        r[t] = idx[idx[t]] / idx[t];
    }
}
