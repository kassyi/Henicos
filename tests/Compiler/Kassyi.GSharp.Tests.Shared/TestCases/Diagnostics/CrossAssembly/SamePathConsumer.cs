extern alias LibA;
using Kassyi.GSharp.Core;

namespace CrossAssemblyConsumer
{
    public static partial class ChainKernels
    {
        [GpuKernel]
        public static void CallKernel(int[] data)
        {
            data[ThreadIndex.X] = LibA::XasmLib.LibMath.Quad(data[ThreadIndex.X]) + XasmLib.LibMath.Helper(1);
        }
    }
}

// The path of the library's internal Helper, declared by the consumer: its own device code takes the same C++ name
namespace XasmLib
{
    public static partial class LibMath
    {
        [GpuDeviceFunction]
        public static int Helper(int x) => x + 100;
    }
}
