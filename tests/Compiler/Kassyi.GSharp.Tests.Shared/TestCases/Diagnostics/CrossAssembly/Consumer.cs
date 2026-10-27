using Kassyi.GSharp.Core;
using XasmLib;

namespace CrossAssemblyConsumer
{
    public static partial class ConsumerKernels
    {
        [GpuKernel]
        public static void CallKernel(int[] data)
        {
            data[ThreadIndex.X] = LibMath.Twice(data[ThreadIndex.X]);
            var pair = new Pair(data[0]);
            data[1] = pair.A;
        }
    }
}
