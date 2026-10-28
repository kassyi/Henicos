#nullable enable
using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Kernels that write to an argument whose writes the CUDA launcher never copies back: an <c>[In]</c> array and a
/// read-only container (#798). Each kernel also reads the argument into <c>o</c>, so a test sees that the kernel ran.
/// </summary>
[SkipSnapshotConsistency]
public static partial class InArrayCopyKernels
{
    [GpuKernel]
    public static void InIntArrayKernel([In] int[] input, int[] o)
    {
        int i = ThreadIndex.X;
        if (i < input.Length)
        {
            o[i] = input[i];
            input[i] = 99;
        }
    }

    [GpuKernel]
    public static void InIntSpanKernel([In] Span<int> input, int[] o)
    {
        int i = ThreadIndex.X;
        if (i < input.Length)
        {
            o[i] = input[i];
            input[i] = 99;
        }
    }

    [GpuKernel]
    public static void InBagArrayKernel([In] ConvertBag[] bags, int[] o)
    {
        int i = ThreadIndex.X;
        if (i < bags.Length)
        {
            o[i] = bags[i].N + bags[i].Data[0];
            bags[i].N = 99;
            bags[i].Data[0] = 77;
        }
    }

    [GpuKernel]
    public static void InNullableBagArrayKernel([In] ConvertBag[]? bags, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
#pragma warning disable CS8602 // The device reads a null array as one of length 0
            o[0] = bags.Length;
#pragma warning restore CS8602
        }
    }

    [GpuKernel]
    public static void ReadOnlySpanBagKernel(ReadOnlySpan<ConvertBag> bags, int[] o)
    {
        int i = ThreadIndex.X;
        if (i < bags.Length)
        {
            o[i] = bags[i].Data[0];
            bags[i].Data[0] = 77;
        }
    }

    [GpuKernel]
    public static void InElemHolderArrayKernel([In] LayoutReproElemHolder[] holders, int[] o)
    {
        int i = ThreadIndex.X;
        if (i < holders.Length)
        {
            o[i] = holders[i].Elems[0].Items[0];
            holders[i].Elems[0].Tag = 99;
            holders[i].Elems[0].Items[0] = 77;
        }
    }

    [GpuKernel]
    public static void InPairHolderArrayKernel([In] ConvertBagPairHolder[] holders, int[] o)
    {
        int i = ThreadIndex.X;
        if (i < holders.Length)
        {
            o[i] = holders[i].Tag + holders[i].Bags[1].Data[0];
            holders[i].Tag = 99;
            holders[i].Bags[1].Data[0] = 77;
        }
    }
}
