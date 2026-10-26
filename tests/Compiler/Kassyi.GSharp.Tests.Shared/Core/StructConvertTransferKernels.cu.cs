#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// V is at offset 4 on the host and 8 on CUDA, so a CUDA array of it is converted element by element.
/// </summary>
public struct ConvertPair
{
    public float A;
    public Vector2 V;
}

/// <summary>
/// The host puts the array reference first, so an array of it is converted and each Data is copied separately.
/// </summary>
public struct ConvertBag
{
    public int N;
    public int[] Data;
}

[InlineArray(2)]
public struct ConvertBagPair
{
    private ConvertBag _element0;
}

/// <summary>An element of a converted array whose inline array holds elements with array fields.</summary>
public struct ConvertBagPairHolder
{
    public int Tag;
    public ConvertBagPair Bags;
}

[SkipSnapshotConsistency]
public static partial class StructConvertTransferKernels
{
    [GpuKernel]
    public static void SpanKernel(Span<ConvertPair> items)
    {
        int i = ThreadIndex.X;
        if (i < items.Length)
        {
            items[i].A = items[i].A + items[i].V.Y;
        }
    }

    [GpuKernel]
    public static void ReadOnlySpanKernel(ReadOnlySpan<ConvertPair> items, float[] o)
    {
        int i = ThreadIndex.X;
        if (i < items.Length)
        {
            o[i] = items[i].A + items[i].V.Y;
        }
    }

    [GpuKernel]
    public static void InArrayKernel([In] ConvertPair[] items, float[] o)
    {
        int i = ThreadIndex.X;
        if (i < items.Length)
        {
            o[i] = items[i].V.Y;
            items[i].A = 100f;
        }
    }

    [GpuKernel]
    public static void EmptyArrayKernel(ConvertPair[]? items, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
#pragma warning disable CS8602 // The device reads a null array as one of length 0
            o[0] = items.Length;
#pragma warning restore CS8602
        }
    }

    [GpuKernel]
    public static void InOutBagKernel(ConvertBag[] bags)
    {
        int i = ThreadIndex.X;
        if (i < bags.Length)
        {
            bags[i].N = bags[i].N + 1;
            bags[i].Data[1] = bags[i].Data[0] * 2;
        }
    }

    [GpuKernel]
    public static void OutBagKernel([Out] ConvertBag[] bags)
    {
        int i = ThreadIndex.X;
        if (i < bags.Length)
        {
            bags[i].N = 5 + i;
            bags[i].Data[0] = 9 + i;
        }
    }

    [GpuKernel]
    public static void BagReadKernel(ConvertBag[] bags, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = bags[0].N;
            o[1] = bags[0].Data.Length;
            o[2] = bags[1].N;
        }
    }

    [GpuKernel]
    public static void PairHolderReadKernel(ConvertBagPairHolder[] holders, int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = holders[0].Tag;
            o[1] = holders[0].Bags[1].Data.Length;
        }
    }

    [GpuKernel]
    public static void OutBagLengthKernel([Out] ConvertBag[] bags, int[] o)
    {
        int i = ThreadIndex.X;
        if (i < bags.Length)
        {
            o[i] = bags[i].Data.Length;
            bags[i].N = 7;
        }
    }
}
