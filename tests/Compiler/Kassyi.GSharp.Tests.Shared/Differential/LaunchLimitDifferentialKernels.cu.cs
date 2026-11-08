// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels for <see cref="LaunchLimitDifferentialTests"/>: launches at and beyond the limits of every GPU (#799).
/// </summary>
public static partial class LaunchLimitDifferentialKernels
{
    // Every thread writes 1 + its linear rank in the launch (x fastest, blocks in the same order), so a thread that
    // does not run, or runs at the wrong place, shows in the output
    [GpuKernel]
    public static void WriteLinearRankKernel(int[] output)
    {
        long threadsPerBlock = (long)BlockDimension.X * BlockDimension.Y * BlockDimension.Z;
        long block = ((long)BlockIndex.Z * GridDimension.Y + BlockIndex.Y) * GridDimension.X + BlockIndex.X;
        long thread = ((long)ThreadIndex.Z * BlockDimension.Y + ThreadIndex.Y) * BlockDimension.X + ThreadIndex.X;
        long rank = block * threadsPerBlock + thread;
        if (rank < output.Length)
        {
            output[(int)rank] = (int)rank + 1;
        }
    }
}
