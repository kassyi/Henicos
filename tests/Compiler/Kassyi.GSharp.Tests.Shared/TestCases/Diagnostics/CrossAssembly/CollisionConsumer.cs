extern alias LibA;
using Kassyi.GSharp.Core;

namespace CrossAssemblyConsumer
{
    public static partial class CollisionKernels
    {
        [GpuKernel]
        public static void CallKernel(int[] data)
        {
            data[ThreadIndex.X] = LibA::XasmLib.LibMath.Twice(data[ThreadIndex.X]);
        }
    }
}
