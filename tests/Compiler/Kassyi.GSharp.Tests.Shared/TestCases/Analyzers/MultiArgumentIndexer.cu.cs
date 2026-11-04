using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

[SkipSnapshotConsistency("Analyzer test case: StackArrayDiagnosticsTests")]
public static partial class MultiArgumentIndexer
{
    public struct Grid
    {
        public int Width;

        public int this[int x, int y] => y * Width + x;
    }

    [GpuKernel]
    public static void IndexerKernel(int[] output, Grid grid)
    {
        output[0] = grid[1, 2];
    }
}
