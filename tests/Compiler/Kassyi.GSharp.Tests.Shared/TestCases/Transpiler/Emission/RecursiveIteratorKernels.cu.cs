using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

[SkipSnapshotConsistency("Recursive iterator failure checked by EmissionShapeTests")]
public static partial class RecursiveIteratorKernels
{
    [GpuDeviceFunction]
    public static IEnumerable<int> Rec(int n)
    {
        if (n > 0)
        {
            foreach (var x in Rec(n - 1))
            {
                yield return x;
            }
        }
        yield return n;
    }

    [GpuKernel]
    public static void SelfRecursiveKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        foreach (var x in Rec(tid))
        {
            output[tid] = x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> MutualA(int n)
    {
        if (n > 0)
        {
            foreach (var x in MutualB(n - 1))
            {
                yield return x;
            }
        }
        yield return n;
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> MutualB(int n)
    {
        if (n > 0)
        {
            foreach (var x in MutualA(n - 1))
            {
                yield return x;
            }
        }
        yield return n;
    }

    [GpuKernel]
    public static void MutualRecursiveKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        foreach (var x in MutualA(tid))
        {
            output[tid] = x;
        }
    }
}
