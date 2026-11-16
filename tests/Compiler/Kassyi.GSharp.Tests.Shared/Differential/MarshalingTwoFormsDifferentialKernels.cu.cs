#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// A probe struct whose constructor takes by-value parameters (Item 1 verification).
/// </summary>
public struct ProbePoint
{
    public int Field;
    public int Value;

    public ProbePoint(int f, int v)
    {
        Field = f;
        Value = v;
    }
}

/// <summary>
/// Kernels used to test two-form CUDA emission and dynamic selection (Issue #1940 / #1842):
/// multi-block execution, ref readonly parameters, separate variable executions,
/// two in parameters, and nested function/ctor ordinal isolation.
/// </summary>
public static partial class MarshalingTwoFormsDifferentialKernels
{
    [GpuDeviceFunction]
    public static int ProbeDeviceAdd(int x) => x + 5;

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
    public static void MultiBlockInAndRefKernel(in int first, ref int second, [Out] int[] observed, [Out] int[] allReads)
    {
        var tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        allReads[tid] = first;

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

    [GpuKernel]
    public static void TwoInAndOneRefKernel(in int first, in int second, ref int third, [Out] int[] observed)
    {
        if (ThreadIndex.X == 0)
        {
            third += 10;
            observed[0] = first;
            observed[1] = second;
        }
    }

    [GpuKernel]
    public static void NestedFunctionsAndCtorKernel(in int first, ref int second, [Out] int[] observed)
    {
        int LocalAdd(int x) => x + 1;

        if (ThreadIndex.X == 0)
        {
            var p = new ProbePoint(first, second);
            var localRes = LocalAdd(p.Field);
            var devRes = ProbeDeviceAdd(localRes);
            second += devRes;
            observed[0] = first;
        }
    }
}
