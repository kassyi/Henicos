using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public struct BagA { public int[] Items; }

    public static partial class PointerNameRefStructKernels
    {
        // Scale is not overloaded, so its public IntPtr launcher is LaunchScalePtr(IntPtr), the same as LaunchScalePtr of ScalePtr
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Scale(ref BagA bag) { bag.Items[ThreadIndex.X] = 1; }

        [GpuKernel]
        public static void ScalePtr(nint address) { }
    }
}
