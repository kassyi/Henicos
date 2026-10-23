using Kassyi.GSharp.Core;
namespace N1
{
    public static partial class K
    {
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Run(int[] data) { data[ThreadIndex.X] = 1; }
    }
}
namespace N2
{
    public static partial class K
    {
        [GpuKernel]
        public static void Run(int[] data) { data[ThreadIndex.X] = 2; }
    }
}
