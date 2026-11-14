using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public struct RefA { public int[] Items; }

    public struct RefB { public int[] Items; }

    public static partial class RefStructKernels
    {
        // A ref to a struct with an array field has no GpuBuffer<T>: both typed launchers take an IntPtr
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Run(ref RefA bag) { }

        [GpuKernel]
        public static void Run(ref RefB bag) { }
    }
}
