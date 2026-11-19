using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// An <c>Action</c> as the parameter of a device function and a reassigned <c>Action</c> local. CPU paths only: the CUDA
/// dialect writes a delegate as <c>auto</c>, which nvcc rejects for a parameter and for a reassigned local.
/// </summary>
[SkipSnapshotConsistency("CPU only: the CUDA dialect writes the delegate as auto, which nvcc rejects here")]
public static partial class ActionDelegateCpuKernels
{
    [GpuDeviceFunction]
    public static void InvokeAction(Action action) => action();

    [GpuKernel]
    public static void ActionParameterKernel(int[] dst)
    {
        int t = ThreadIndex.X;
        InvokeAction(() => dst[t] = t + 2);
    }

    [GpuKernel]
    public static void ActionReassignedKernel(int[] dst)
    {
        int t = ThreadIndex.X;
        Action f = () => dst[t] = 1;
        if (t == 1)
        {
            f = () => dst[t] = 7;
        }

        f();
    }
}
