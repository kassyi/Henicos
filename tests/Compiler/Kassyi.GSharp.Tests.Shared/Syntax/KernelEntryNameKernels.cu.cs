// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax.GlobalNames;

// Entry points used to be {Type}_{Method}: both kernels below were exported as Entry_Host_RunKernel.
public static partial class Entry_Host
{
    [GpuKernel]
    public static void RunKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = 100 + tid;
    }
}

public static partial class Entry
{
    [GpuKernel]
    public static void Host_RunKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = 200 + tid;
    }
}

// Overloaded kernels get one entry point each; the host wrappers are selected by C# overload resolution.
public static partial class OverloadedKernels
{
    [GpuKernel]
    public static void FillKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = tid * 2;
    }

    [GpuKernel]
    public static void FillKernel(float[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = tid * 0.5f;
    }
}
