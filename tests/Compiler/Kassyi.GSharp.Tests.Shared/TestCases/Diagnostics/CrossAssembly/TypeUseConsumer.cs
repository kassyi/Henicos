extern alias LibA;
using Kassyi.GSharp.Core;

namespace CrossAssemblyConsumer
{
    public static partial class TypeUseKernels
    {
        [GpuKernel]
        public static void CallKernel(int[] data)
        {
            LibA::XasmLib.Pair pair = default;
            data[0] = pair.A;
        }
    }
}
