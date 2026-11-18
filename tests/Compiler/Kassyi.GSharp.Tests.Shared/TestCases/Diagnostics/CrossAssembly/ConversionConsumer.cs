using Kassyi.GSharp.Core;
using XasmLib;

namespace CrossAssemblyConsumer
{
    public static partial class ConsumerKernels
    {
        [GpuKernel]
        public static void ConvertKernel(double[] data, int[] intData)
        {
            var m1 = (Meters)data[ThreadIndex.X];
            data[ThreadIndex.X] = (double)m1;

            Meters a = intData[0];
            double b = Take(intData[1]);
            double d = a;
            data[0] = b + d;

            var box = (Box<double>)data[1];
            data[1] = box.Value;

            Meters? nullableMeters = m1;
            var nd = (double?)nullableMeters;
            data[2] = nd.HasValue ? nd.Value : 0.0;
        }

        [GpuDeviceFunction]
        private static double Take(Meters m) => m.V;
    }
}
