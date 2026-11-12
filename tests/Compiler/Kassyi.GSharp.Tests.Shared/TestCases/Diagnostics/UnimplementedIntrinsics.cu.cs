using Kassyi.GSharp.Core;

namespace UnimplementedIntrinsicsTest
{
    public static partial class Kernels
    {
        // mbarrier, WMMA and the shared window address are declared unsupported on the CPU dialect (ptx_intrinsics.yml,
        // wmma_intrinsics.yml): each call is a GSH0054 warning there and a runtime failure, and nothing on CUDA
        [GpuDeviceFunction]
        public static void ArriveAndWait(ref ulong barrier)
        {
            Gpu.MBarrierArrive(ref barrier);
            Gpu.MBarrierWait(ref barrier, 0);
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void UnimplementedKernel(int[] data, int[] c)
        {
            var bar = new SharedMemory<ulong>(1);
            Gpu.MBarrierInit(ref bar[0], 32);
            ArriveAndWait(ref bar[0]);
            WmmaAccumulator<WmmaM16N16K16, int> acc = default;
            Wmma.Fill(ref acc, 0);
            Wmma.Store(ref c[0], in acc, 16, WmmaMemoryLayout.RowMajor);
            data[0] = Gpu.ConvertToSharedMemory(ref data[1]) == 0 ? 1 : 2;
            data[2] = Gpu.PopCount(7u) == 3u ? 1 : 0;
        }
    }
}
