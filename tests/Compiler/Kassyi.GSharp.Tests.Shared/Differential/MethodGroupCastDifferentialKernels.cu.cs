#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A delegate type with a ref parameter, an out parameter, an in parameter and a ref result.</summary>
public delegate int DiffByRefFn(ref int total, out int doubled, in int step);

/// <summary>
/// A delegate type declared by the user, which converts from a device function like <c>Func</c> does.
/// </summary>
public delegate int DiffBumpFn(int value);

/// <summary>Differential kernels for a device function named through a cast to its delegate type.</summary>
public static partial class MethodGroupCastDifferentialKernels
{
    [GpuDeviceFunction]
    public static int Bump(int x) => (x * 3) + 1;

    // (Func<int, int>)Bump converts the method group as Func<int, int> f = Bump does
    [GpuKernel]
    public static void MethodGroupCastKernel(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        var cast = (Func<int, int>)Bump;
        Func<int, int> assigned = Bump;
        r[tid] = cast(inputs[tid] % 1000) - assigned(1);
    }

    [GpuDeviceFunction]
    public static int Accumulate(ref int total, out int doubled, in int step)
    {
        total += step;
        doubled = total * 2;
        return total + doubled;
    }

    // A delegate with ref, out and in parameters, and a delegate that is null: both the null cast and the null test
    [GpuKernel]
    public static void ByRefDelegateKernel(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        DiffByRefFn accumulate = Accumulate;
        int total = v;
        int step = 3;
        int result = accumulate(ref total, out int doubled, in step);
        var missing = (DiffByRefFn)null;
        r[tid] = result + (total * 3) + doubled + (missing == null ? 1000 : 0) + (accumulate != null ? 5000 : 0);
    }

    // A delegate type declared by the user: assigned, cast from the method group, and cast from another delegate
    [GpuKernel]
    public static void UserDelegateKernel(int[] inputs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= inputs.Length)
        {
            return;
        }

        int v = inputs[tid] % 1000;
        DiffBumpFn assigned = Bump;
        var cast = (DiffBumpFn)Bump;
        var fromDelegate = (DiffBumpFn)assigned;
        r[tid] = assigned(v) + (cast(v) * 2) + (fromDelegate(v) * 3);
    }
}
