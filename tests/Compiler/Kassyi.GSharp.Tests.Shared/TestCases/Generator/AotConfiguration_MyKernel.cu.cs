using Kassyi.GSharp.Core;
namespace AotTest {
    public partial class MyKernel {
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Run(float[] data) {}
    }
}
