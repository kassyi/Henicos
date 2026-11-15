using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public static partial class SourceConstantKernels
    {
        // With GSharpEmitSource, LaunchX gets the constant LaunchX_Source, the name of the launcher of X_Source
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void LaunchX(int[] data) { data[ThreadIndex.X] = 1; }

        [GpuKernel]
        public static void X_Source(int[] data) { data[ThreadIndex.X] = 2; }
    }
}
