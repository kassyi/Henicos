using Kassyi.GSharp.Core;
using XasmLib;

namespace CrossAssemblyConsumer
{
    public static partial class DivergentCallKernels
    {
        [GpuKernel]
        public static void DivergentCallKernel(int[] data)
        {
            data[0] = LibMath.Twice(ThreadIndex.X);
            data[1] = LibMath.Twice(0);
            Gpu.SyncThreads();
        }
    }
}
