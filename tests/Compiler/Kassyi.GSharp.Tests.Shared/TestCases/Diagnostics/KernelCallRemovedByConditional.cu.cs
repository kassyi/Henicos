#define KERNEL_CALL_DEFINED
using System.Diagnostics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// C# removes the calls of RemovedKernel (its symbol is defined nowhere), arguments included, and keeps those of
// KeptKernel: GSH0003 in device code and GSH0031 in host code only for the kept calls
public static partial class KernelCallRemovedByConditionalKernels
{
    [GpuKernel]
    [Conditional("KERNEL_CALL_NEVER_DEFINED")]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void RemovedKernel(int[] data)
    {
        data[1] = ThreadIndex.X + 100;
    }

    [GpuKernel]
    [Conditional("KERNEL_CALL_DEFINED")]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void KeptKernel(int[] data)
    {
        data[1] = ThreadIndex.X + 100;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CallerKernel(int[] data)
    {
        data[0] = 42;
        RemovedKernel(data);
        KeptKernel(data);
    }
}

public static class KernelCallRemovedByConditionalHost
{
    public static void Run(int[] data)
    {
        KernelCallRemovedByConditionalKernels.RemovedKernel(data);
        KernelCallRemovedByConditionalKernels.KeptKernel(data);
    }
}
