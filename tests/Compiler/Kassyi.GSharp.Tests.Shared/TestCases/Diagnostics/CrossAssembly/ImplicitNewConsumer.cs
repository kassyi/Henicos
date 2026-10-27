using Kassyi.GSharp.Core;
using XasmLib;

namespace CrossAssemblyConsumer
{
    public static partial class ConsumerKernels
    {
        [GpuKernel]
        public static void CallKernel(int[] data)
        {
            Pair pair = new(data[0]);
            data[1] = pair.A;
        }
    }
}
