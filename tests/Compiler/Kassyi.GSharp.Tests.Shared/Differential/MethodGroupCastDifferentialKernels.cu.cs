#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>A delegate type declared by the user, which converts from a device function like <c>Func</c> does.</summary>
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
