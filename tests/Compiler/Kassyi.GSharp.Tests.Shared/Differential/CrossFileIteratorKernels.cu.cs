#nullable enable
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels enumerating cross-file iterators defined in <see cref="CrossFileIteratorLibrary"/>.
/// </summary>
[SkipSnapshotConsistency("Needs its iterator from another file")]
public static partial class CrossFileIteratorKernels
{
    public static readonly string[] CallArgColumns = ["sum_call_arg"];
    public static readonly string[] TwiceColumns = ["sum_twice"];

    /// <summary>
    /// Kernel enumerating <see cref="CrossFileIteratorLibrary.OuterWithCallArg(int)"/> (Shape a).
    /// </summary>
    [GpuKernel]
    public static void CrossFileIteratorCallArgKernel(int[] input, int[] output)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= input.Length)
        {
            return;
        }

        int n = input[tid];
        int sum = 0;
        foreach (var x in CrossFileIteratorLibrary.OuterWithCallArg(n))
        {
            sum += x;
        }

        output[tid] = sum;
    }

    /// <summary>
    /// Kernel enumerating <see cref="CrossFileIteratorLibrary.OuterTwice(int)"/> (Shape b).
    /// </summary>
    [GpuKernel]
    public static void CrossFileIteratorTwiceKernel(int[] input, int[] output)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= input.Length)
        {
            return;
        }

        int n = input[tid];
        int sum = 0;
        foreach (var x in CrossFileIteratorLibrary.OuterTwice(n))
        {
            sum += x;
        }

        output[tid] = sum;
    }
}
