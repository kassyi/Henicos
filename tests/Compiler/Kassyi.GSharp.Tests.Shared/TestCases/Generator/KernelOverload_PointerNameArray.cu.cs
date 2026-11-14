using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public static partial class PointerNameArrayKernels
    {
        // LaunchScalePtr of Scale takes a GSharpArrayProxy, LaunchScalePtr of ScalePtr an IntPtr: no collision
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Scale(float[] data) { data[ThreadIndex.X] *= 2f; }

        [GpuKernel]
        public static void ScalePtr(nint address) { }
    }
}
