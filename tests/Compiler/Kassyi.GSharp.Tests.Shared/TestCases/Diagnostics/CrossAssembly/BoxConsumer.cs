using Kassyi.GSharp.Core;
using XasmLib;

namespace CrossAssemblyConsumer
{
    public static partial class BoxKernels
    {
        [GpuKernel]
        public static void BoxKernel(int[] data)
        {
            var known = new Box<int>(data[0]);
            var other = new Box<float>(1f);
            data[1] = known.Value + (int)other.Value + LibMath.Unbox(2);
        }

        [GpuKernel]
        public static void IteratorKernel(int[] data)
        {
            int sum = 0;
            foreach (var i in LibMath.Upto(4))
            {
                sum += i;
            }

            data[0] = sum;
        }

        [GpuKernel]
        public static void SingleThreadKernel(int[] data)
        {
            if (ThreadIndex.X == 0)
            {
                Wrapper(data.Length);
            }
        }

        [GpuDeviceFunction]
        private static void Wrapper(int n)
        {
            LibMath.GridTouch(n);
        }
    }
}
