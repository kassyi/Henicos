using Kassyi.GSharp.Core;
namespace OverloadTest
{
    public static partial class ErrorSiteKernels
    {
        // A class whose kernels have run-time checks gets the field s_errorSites
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void s_errorSites(int[] data, int index) { data[index] = 1; }
    }
}
