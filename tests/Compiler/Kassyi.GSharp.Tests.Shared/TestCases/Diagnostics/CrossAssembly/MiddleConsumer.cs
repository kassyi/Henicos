using Kassyi.GSharp.Core;

namespace CrossAssemblyConsumer
{
    public static partial class MiddleKernels
    {
        [GpuKernel]
        public static void CallKernel(int[] data)
        {
            data[ThreadIndex.X] = XasmMiddle.LibMiddle.IncTwice(data[ThreadIndex.X]);
        }
    }
}
