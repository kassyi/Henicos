using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public static partial class K
    {
        // Both overloads take one int buffer in the typed Ptr wrapper.
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Run(int[] data) { data[ThreadIndex.X] = 1; }

        [GpuKernel]
        public static void Run(ref int value) { value = 2; }
    }
}
