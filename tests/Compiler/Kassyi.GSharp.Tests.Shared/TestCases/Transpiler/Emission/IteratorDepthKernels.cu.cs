using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

// L1 enumerates L2, ..., L16 enumerates L17: a foreach over L(k) expands 18 - k iterators inside each other
[SkipSnapshotConsistency("Expansion depth checked by EmissionShapeTests")]
public static partial class IteratorDepthKernels
{
    [GpuDeviceFunction]
    public static IEnumerable<int> L1(int n)
    {
        foreach (var x1 in L2(n))
        {
            yield return x1 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L2(int n)
    {
        foreach (var x2 in L3(n))
        {
            yield return x2 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L3(int n)
    {
        foreach (var x3 in L4(n))
        {
            yield return x3 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L4(int n)
    {
        foreach (var x4 in L5(n))
        {
            yield return x4 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L5(int n)
    {
        foreach (var x5 in L6(n))
        {
            yield return x5 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L6(int n)
    {
        foreach (var x6 in L7(n))
        {
            yield return x6 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L7(int n)
    {
        foreach (var x7 in L8(n))
        {
            yield return x7 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L8(int n)
    {
        foreach (var x8 in L9(n))
        {
            yield return x8 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L9(int n)
    {
        foreach (var x9 in L10(n))
        {
            yield return x9 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L10(int n)
    {
        foreach (var x10 in L11(n))
        {
            yield return x10 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L11(int n)
    {
        foreach (var x11 in L12(n))
        {
            yield return x11 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L12(int n)
    {
        foreach (var x12 in L13(n))
        {
            yield return x12 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L13(int n)
    {
        foreach (var x13 in L14(n))
        {
            yield return x13 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L14(int n)
    {
        foreach (var x14 in L15(n))
        {
            yield return x14 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L15(int n)
    {
        foreach (var x15 in L16(n))
        {
            yield return x15 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L16(int n)
    {
        foreach (var x16 in L17(n))
        {
            yield return x16 + 1;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> L17(int n)
    {
        yield return n + 1;
    }

    // 16 nested expansions (L2 to L17)
    [GpuKernel]
    public static void SixteenLevelsKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        foreach (var x in L2(tid))
        {
            output[tid] = x;
        }
    }

    // 17 nested expansions (L1 to L17)
    [GpuKernel]
    public static void SeventeenLevelsKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        foreach (var x in L1(tid))
        {
            output[tid] = x;
        }
    }

    // 17 expansions next to each other, each 1 level deep: siblings do not add up
    [GpuKernel]
    public static void SiblingsKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int sum = 0;
        foreach (var x0 in L17(tid)) { sum += x0; }
        foreach (var x1 in L17(tid)) { sum += x1; }
        foreach (var x2 in L17(tid)) { sum += x2; }
        foreach (var x3 in L17(tid)) { sum += x3; }
        foreach (var x4 in L17(tid)) { sum += x4; }
        foreach (var x5 in L17(tid)) { sum += x5; }
        foreach (var x6 in L17(tid)) { sum += x6; }
        foreach (var x7 in L17(tid)) { sum += x7; }
        foreach (var x8 in L17(tid)) { sum += x8; }
        foreach (var x9 in L17(tid)) { sum += x9; }
        foreach (var x10 in L17(tid)) { sum += x10; }
        foreach (var x11 in L17(tid)) { sum += x11; }
        foreach (var x12 in L17(tid)) { sum += x12; }
        foreach (var x13 in L17(tid)) { sum += x13; }
        foreach (var x14 in L17(tid)) { sum += x14; }
        foreach (var x15 in L17(tid)) { sum += x15; }
        foreach (var x16 in L17(tid)) { sum += x16; }
        output[tid] = sum;
    }
}
