// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Generator;

public static partial class SourceGeneratorTestKernels
{
    [GpuKernel]
    public static void MyTestKernel(int[] a, int[] b, int[] c)
    {
        c[0] = a[0] + b[0];
    }
}

