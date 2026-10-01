// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

public static partial class PtxEngineExperimentKernels
{
    [GpuKernel]
    public static void AddKernel(int[] a, int[] b, int[] c)
    {
        c[0] = a[0] + b[0];
    }
}
