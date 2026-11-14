using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public static partial class MemberKeyKernels
    {
        // The first overload of Run is keyed Run__o0, the key of the kernel named Run__o0
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Run(int[] data) { data[ThreadIndex.X] = 1; }

        [GpuKernel]
        public static void Run(float[] data) { data[ThreadIndex.X] = 2f; }

        [GpuKernel]
        public static void Run__o0(long value) { }
    }
}
