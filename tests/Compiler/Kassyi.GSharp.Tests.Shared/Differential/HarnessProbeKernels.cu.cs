// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels that test the differential harness itself (<see cref="HarnessTests"/>): one whose result every path must
/// reproduce, and one whose result depends on the thread order, which the oracle's order check must reject.
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
}
