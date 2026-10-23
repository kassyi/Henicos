using Kassyi.GSharp.Core;

namespace DialectDiagnosticsTest
{
    public static partial class Kernels
    {
        // Gpu.InlinePtx is CUDA only: the C++ dialect replaces it with a runtime failure and reports GSH0054
        [GpuDeviceFunction]
        public static void PtxInDevice()
        {
            Gpu.InlinePtx("nop;");
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void PtxKernel(int[] data)
        {
            Gpu.InlinePtx("nop;");
            PtxInDevice();
        }
    }
}
