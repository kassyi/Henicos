using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public static partial class ClassMemberKernels
    {
        // Every class with kernels gets the nested class AotContexts
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void AotContexts(int[] data) { data[ThreadIndex.X] = 1; }

        [GpuKernel]
        public static void Fill(int[] data) { data[ThreadIndex.X] = 2; }
    }
}
