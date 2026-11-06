using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// A kernel launched only by <see cref="ModuleContextTests"/>, so that its launcher has resolved nothing when a test
/// process starts.
/// </summary>
[SkipSnapshotConsistency]
public static partial class ModuleContextKernels
{
    [GpuKernel]
    public static void AddKernel(int[] a, int[] b, int[] c)
    {
        int i = ThreadIndex.X;
        c[i] = a[i] + b[i];
    }
}
