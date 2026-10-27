extern alias LibA;
using Kassyi.GSharp.Core;

namespace CrossAssemblyConsumer
{
    public static partial class ChainKernels
    {
        [GpuKernel]
        public static void CallKernel(int[] data)
        {
            data[ThreadIndex.X] = LibA::XasmLib.LibMath.Quad(data[ThreadIndex.X]);
        }
    }
}
