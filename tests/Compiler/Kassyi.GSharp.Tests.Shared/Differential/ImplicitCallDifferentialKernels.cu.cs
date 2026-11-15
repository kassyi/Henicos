#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// A collection type that a device function builds from the elements of a collection expression: the sum of each
/// element times its position (from 1) and the element count.
/// </summary>
[CollectionBuilder(typeof(ImplicitCallDifferentialKernels), nameof(ImplicitCallDifferentialKernels.CreateWeightedBag))]
public struct DiffWeightedBag
{
    public int Sum;
    public int Count;

    public readonly IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
}

/// <summary>A window on one element of an array, pinned by an extension <c>GetPinnableReference</c>.</summary>
public struct DiffPinnedWindow
{
    public int[] Data;
    public int Offset;
}

/// <summary>
/// A value pinned in place by an extension <c>GetPinnableReference</c> with a <c>ref this</c> receiver.
/// </summary>
public struct DiffPinnedCell
{
    public int Value;
}

/// <summary>A window pinned by an extension <c>GetPinnableReference</c> that returns <c>ref readonly</c>.</summary>
public struct DiffReadOnlyWindow
{
    public int[] Data;
    public int Offset;
}

/// <summary>A value that no variable holds when it is pinned: <c>fixed (int* p = Make(v))</c>.</summary>
public struct DiffRvalueCell
{
    public int Value;
}

/// <summary>
/// Differential kernels for the calls that C# makes without a call in the source: the <c>[CollectionBuilder]</c> method
/// of a collection expression, and the <c>GetPinnableReference</c> of a <c>fixed</c> statement.
/// </summary>
public static unsafe partial class ImplicitCallDifferentialKernels
{
    public static readonly string[] BuilderColumns = ["declared", "empty", "argument", "order"];

    public static readonly string[] PinnableColumns = ["window", "cell", "read_only", "rvalue"];

    [GpuDeviceFunction]
    public static DiffWeightedBag CreateWeightedBag(ReadOnlySpan<int> values)
    {
        var bag = new DiffWeightedBag();
        for (int i = 0; i < values.Length; i++)
        {
            bag.Sum += values[i] * (i + 1);
        }

        bag.Count = values.Length;
        return bag;
    }

    [GpuDeviceFunction]
    public static int Weigh(DiffWeightedBag bag) => (bag.Sum * 8) + bag.Count;

    [GpuDeviceFunction]
    public static int Step(ref int k, int by)
    {
        k = (k * 3) + by;
        return k;
    }

    [GpuDeviceFunction]
    public static ref int GetPinnableReference(this DiffPinnedWindow window) => ref window.Data[window.Offset];

    [GpuDeviceFunction]
    public static ref int GetPinnableReference(ref this DiffPinnedCell cell) => ref cell.Value;

    [GpuDeviceFunction]
    public static ref readonly int GetPinnableReference(this DiffReadOnlyWindow window) =>
        ref window.Data[window.Offset];

    [GpuDeviceFunction]
    public static ref readonly int GetPinnableReference(in this DiffRvalueCell cell) => ref cell.Value;

    [GpuDeviceFunction]
    public static DiffRvalueCell MakeRvalueCell(int value)
    {
        DiffRvalueCell cell;
        cell.Value = value;
        return cell;
    }

    // A collection expression declared, empty, passed as an argument, and with elements that change a local in order
    [GpuKernel]
    public static void CollectionBuilderKernel(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        int o = tid * 4;
        DiffWeightedBag bag = [v, v + 1, v * 2];
        DiffWeightedBag empty = [];
        r[o + 0] = Weigh(bag);
        r[o + 1] = Weigh(empty);
        r[o + 2] = Weigh([v - 3]);
        int k = v % 7;
        r[o + 3] = Weigh([Step(ref k, 1), Step(ref k, 2), k]);
    }

    // fixed over a window into the output, over a local through ref this, over a ref readonly element of the input,
    // and over values that no variable holds
    [GpuKernel]
    public static void PinnableReferenceKernel(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        int o = tid * 4;
        var window = new DiffPinnedWindow { Data = r, Offset = o };
        fixed (int* p = window)
        {
            *p = v * 2;
        }

        var cell = new DiffPinnedCell { Value = v };
        fixed (int* p = cell)
        {
            *p += 5;
        }

        r[o + 1] = cell.Value;
        var readOnly = new DiffReadOnlyWindow { Data = inputs, Offset = tid };
        fixed (int* p = readOnly)
        {
            r[o + 2] = (*p % 1000) - 1;
        }

        // The receivers are values that no variable holds: each copy lives for the whole statement, as in C#
        fixed (int* p = MakeRvalueCell(v + 17), q = MakeRvalueCell(v + 18))
        {
            r[o + 3] = (*p * 3) + *q;
        }
    }
}
