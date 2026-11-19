using Kassyi.GSharp.Core;
using SpillSuppressionLibrary;

namespace SpillSuppressionConsumer
{
    public static partial class Kernels
    {
#pragma warning disable GS0001
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void MarkedKernel(int[] d)
        {
            d[0] = Lib.F(d[1]) + Lib.Relay(d[2]);
        }
#pragma warning restore GS0001

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedKernel(int[] d)
        {
            d[0] = Lib.G(d[1]);
        }
    }
}
