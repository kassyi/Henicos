using Kassyi.GSharp.Core;
using XasmLib;

namespace CrossAssemblyConsumer
{
    public static partial class ConsumerKernels
    {
        [GpuKernel]
        public static void ConvertKernel(double[] data, int[] intData)
        {
            Meters a = intData[0];
            double d = a;
            data[0] = d;
        }
    }
}
