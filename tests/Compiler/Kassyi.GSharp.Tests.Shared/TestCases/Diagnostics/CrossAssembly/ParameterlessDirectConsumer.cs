using Kassyi.GSharp.Core;
using XasmLib;

namespace CrossAssemblyConsumer
{
    public static partial class ConsumerKernels
    {
        [GpuKernel]
        public static void CountKernel(int[] data)
        {
            var counter = new Counter();
            data[0] = counter.Value;
        }
    }
}
