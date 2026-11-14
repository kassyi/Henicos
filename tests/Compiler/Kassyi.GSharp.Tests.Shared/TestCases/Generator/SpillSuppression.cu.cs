using Kassyi.GSharp.Core;

namespace SpillSuppressionTest
{
    public static partial class Kernels
    {
        // Reached only from MarkedKernel, through Own
        [GpuDeviceFunction]
        public static int Leaf(int x) => x + 1;

        [GpuDeviceFunction]
        public static int Own(int x) => Leaf(x) * 3;

        // Reached from MarkedKernel and from UnmarkedKernel
        [GpuDeviceFunction]
        public static int Shared(int x) => x * 2;

        // Recursive, reached only from UnmarkedKernel
        [GpuDeviceFunction]
        public static int Countdown(int x) => x <= 0 ? 0 : Countdown(x - 1) + 1;

#pragma warning disable GS0001
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void MarkedKernel(int[] data)
        {
            data[0] = Own(data[0]) + Shared(data[1]);
        }
#pragma warning restore GS0001

        // Its name starts with the name of the marked kernel
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void MarkedKernelTwin(int[] data)
        {
            data[0] = data[1];
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedKernel(int[] data)
        {
            data[0] = Shared(data[1]) + Countdown(data[2]);
        }
    }
}
