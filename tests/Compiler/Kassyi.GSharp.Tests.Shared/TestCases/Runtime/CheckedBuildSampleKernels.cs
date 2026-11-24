using Kassyi.GSharp.Core;

namespace SampleNamespace;

public static partial class SampleKernels
{
    [GpuKernel]
    public static void VoidScalarKernel(int value)
    {
    }
}
