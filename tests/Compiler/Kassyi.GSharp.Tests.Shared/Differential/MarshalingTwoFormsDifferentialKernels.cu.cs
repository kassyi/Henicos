#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels used to test two-form CUDA emission and dynamic selection (Issue #1940 / #1842):
/// multi-block execution, ref readonly parameters, and separate variable executions.
/// </summary>
public static partial class MarshalingTwoFormsDifferentialKernels
{
    [GpuKernel]
    public static void InAndRefSeparateVariablesKernel(in int first, ref int second, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            second += 10;
            observed[0] = first;
        }
    }

    [GpuKernel]
    public static void MultiBlockInAndRefKernel(in int first, ref int second, [Out] int[] observed)
    {
        if (BlockIndex.X == 0 && ThreadIndex.X == 0)
        {
            second += 10;
            observed[0] = first;
        }
    }

    [GpuKernel]
    public static void RefReadOnlyAndRefKernel(ref readonly int first, ref int second, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            second += 10;
            observed[0] = first;
        }
    }
}
