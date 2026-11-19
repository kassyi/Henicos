using Kassyi.GSharp.Core;
using SpillSuppressionLibrary;

namespace SpillSuppressionConsumer
{
    // MarkedKernel calls each library function directly; each unmarked kernel reaches one of them in one way only
    public static partial class Kernels
    {
#pragma warning disable GS0001
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void MarkedKernel(int[] d)
        {
            d[0] = Lib.F(d[1]) + Lib.Relay(d[2]) + Lib.F2(d[1]) + Lib.FGetter(d[1]) + Lib.FViaFn(d[1]) +
                Lib.FCtor(d[1]);
        }
#pragma warning restore GS0001

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedKernel(int[] d)
        {
            d[0] = Lib.G(d[1]);
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedChainKernel(int[] d)
        {
            d[0] = Lib.G2(d[1]);
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedGetterKernel(int[] d)
        {
            d[0] = new LibHolder { V = d[1] }.Doubled;
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedViaFunctionKernel(int[] d)
        {
            d[0] = Lib.UsesGetter(d[1]);
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void UnmarkedConstructorKernel(int[] d)
        {
            d[0] = new LibBuilt(d[1]).V;
        }
    }
}
