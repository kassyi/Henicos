// ReSharper disable All
using Kassyi.GSharp.Core;

// The same kernel class and method name in two namespaces used to share one generated file (K.g.cs) and one entry
// point (K_Run).
namespace Kassyi.GSharp.Tests.Shared.Syntax.GlobalNames.First
{
    public static partial class SameNamedKernels
    {
        [GpuKernel]
        public static void RunKernel(int[] output)
        {
            int tid = ThreadIndex.X;
            output[tid] = 300 + tid;
        }
    }
}

namespace Kassyi.GSharp.Tests.Shared.Syntax.GlobalNames.Second
{
    public static partial class SameNamedKernels
    {
        [GpuKernel]
        public static void RunKernel(int[] output)
        {
            int tid = ThreadIndex.X;
            output[tid] = 400 + tid;
        }
    }
}
