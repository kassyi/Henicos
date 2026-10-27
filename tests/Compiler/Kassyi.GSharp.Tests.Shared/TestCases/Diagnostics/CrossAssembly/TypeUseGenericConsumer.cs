extern alias LibA;
using Kassyi.GSharp.Core;

namespace CrossAssemblyConsumer
{
    public struct Mine
    {
        public LibA::XasmLib.Pair P;
    }

    public static partial class TypeUseKernels
    {
        [GpuKernel]
        public static void CallKernel(int[] data)
        {
            LibA::XasmLib.Box<int> box = default;
            Mine mine = default;
            data[0] = box.Value + mine.P.A;
        }
    }
}
