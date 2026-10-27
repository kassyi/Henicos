using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

// L1 enumerates L2, ..., L16 enumerates L17: a foreach over L(k) expands 18 - k iterators inside each other
[SkipSnapshotConsistency("Expansion depth overflow checked by EmissionShapeTests")]
public static partial class IteratorDepthOverflowKernels
{
    [GpuDeviceFunction]
    public static IEnumerable<int> L1(int n)
    {
        foreach (var x in L2(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L2(int n)
    {
        foreach (var x in L3(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L3(int n)
    {
        foreach (var x in L4(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L4(int n)
    {
        foreach (var x in L5(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L5(int n)
    {
        foreach (var x in L6(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L6(int n)
    {
        foreach (var x in L7(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L7(int n)
    {
        foreach (var x in L8(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L8(int n)
    {
        foreach (var x in L9(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L9(int n)
    {
        foreach (var x in L10(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L10(int n)
    {
        foreach (var x in L11(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L11(int n)
    {
        foreach (var x in L12(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L12(int n)
    {
        foreach (var x in L13(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L13(int n)
    {
        foreach (var x in L14(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L14(int n)
    {
        foreach (var x in L15(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L15(int n)
    {
        foreach (var x in L16(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L16(int n)
    {
        foreach (var x in L17(n))
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L17(int n)
    {
        yield return n;
    }

    // 17 nested expansions (L1 to L17): one more than the maximum
    [GpuKernel]
    public static void SeventeenLevelsKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        foreach (var x in L1(tid))
        {
            output[tid] = x;
        }
    }
}
