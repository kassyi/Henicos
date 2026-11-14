using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public static partial class PointerNameScalarKernels
    {
        // LaunchScalePtr of Scale and LaunchScalePtr of ScalePtr both take one IntPtr
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void Scale(nint address) { }

        [GpuKernel]
        public static void ScalePtr(nint address) { }
    }
}
