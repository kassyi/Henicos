using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

[SkipSnapshotConsistency("Analyzer test case: StackArrayDiagnosticsTests")]
public static partial class MultidimensionalArrayParameters
{
    public struct ImageBuffer
    {
        public float[,] Pixels;
    }

    public struct NestedImageBuffer
    {
        public ImageBuffer Inner;
    }

    [GpuDeviceFunction]
    public static int ReadVolume(int[,,] volume)
    {
        return volume[1, 2, 3];
    }

    [GpuDeviceFunction]
    public static float ReadBuffer(ImageBuffer buf)
    {
        return buf.Pixels[0, 1];
    }

    [GpuKernel]
    public static void MatrixKernel(int[,] grid, ImageBuffer buf, NestedImageBuffer nested, int[] output)
    {
        ImageBuffer local = default;
        output[0] = grid[1, 0] + (int)buf.Pixels[0, 1] + (int)nested.Inner.Pixels[0, 0] + (int)local.Pixels[0, 0];
    }
}
