// ReSharper disable All
using Kassyi.GSharp.Core;

namespace TestNamespace
{
    public static partial class TestKernels
    {
        [GpuKernel]
        public static void AddKernel(int[] a, int[] b, int[] c)
        {
            c[0] = a[0] + b[0];
        }
    }
}
