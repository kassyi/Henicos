using Kassyi.GSharp.Core;

namespace XasmApp
{
    public static partial class HoldKernels
    {
        [GpuKernel]
        public static void HoldKernel(int[] data)
        {
            data[ThreadIndex.X] = XasmB.LibB.Hold(data[ThreadIndex.X]);
        }
    }
}
