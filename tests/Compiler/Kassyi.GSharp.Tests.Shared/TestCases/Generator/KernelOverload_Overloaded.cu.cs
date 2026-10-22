using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public static partial class K
    {
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Run(int[] data) { data[ThreadIndex.X] = 1; }

        [GpuKernel]
        public static void Run(float[] data) { data[ThreadIndex.X] = 2f; }
    }
}
