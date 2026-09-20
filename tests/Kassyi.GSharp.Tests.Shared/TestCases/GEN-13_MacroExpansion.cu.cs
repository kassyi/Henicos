using Kassyi.GSharp.Core;

namespace TestSpace
{
    public static class MacroExpansionKernel
    {
        [GpuKernel]
        public static void TestMacroKernel(int[] data, int length)
        {
            // GridStrideLoop test
            Gpu.GridStrideLoop(length, i =>
            {
                data[i] = i;
            });

            // SyncGrid test
            Gpu.SyncGrid();

            // SingleThread test
            Gpu.SingleThread(() =>
            {
                data[0] = 999;
            });

            // InlinePtx test
            Gpu.InlinePtx("/* inline ptx here */");
        }
    }
}
