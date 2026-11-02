#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Boundary and sad-path launch limit verification for Lane D1 Stage 3 (<c>D1-7a</c>).
/// Hardware upper-limit validation (<c>D1-7b</c>: <c>gridDimY &gt; 65535</c>, <c>blockDimZ &gt; 64</c>,
/// <c>ThreadsPerBlock &gt; 1024</c>, <c>MaxCpuThreads</c>, and <c>void</c>-kernel zero-dimension checks)
/// is deferred pending N5 (<c>#799</c>).
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class LaunchLimitDifferentialTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0u, 1u, 1u, 1u, 1u, 1u)]
    [InlineData(1u, 0u, 1u, 1u, 1u, 1u)]
    [InlineData(1u, 1u, 0u, 1u, 1u, 1u)]
    [InlineData(1u, 1u, 1u, 0u, 1u, 1u)]
    [InlineData(1u, 1u, 1u, 1u, 0u, 1u)]
    [InlineData(1u, 1u, 1u, 1u, 1u, 0u)]
    public void ZeroDimensionLaunch_AutoResultKernel_ThrowsArgumentOutOfRangeException(
        uint gridX,
        uint gridY,
        uint gridZ,
        uint blockX,
        uint blockY,
        uint blockZ)
    {
        int[] src = [1, 2, 3, 4];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LaunchShapeDifferentialKernels.LaunchAutoResultThreeDimensionalKernel(
                src,
                salt: 42,
                gridDimX: gridX,
                gridDimY: gridY,
                gridDimZ: gridZ,
                blockDimX: blockX,
                blockDimY: blockY,
                blockDimZ: blockZ));

#if GSHARP_CPU
        bool prev = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LaunchShapeDifferentialKernels.LaunchAutoResultThreeDimensionalKernel(
                    src,
                    salt: 42,
                    gridDimX: gridX,
                    gridDimY: gridY,
                    gridDimZ: gridZ,
                    blockDimX: blockX,
                    blockDimY: blockY,
                    blockDimZ: blockZ));
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = prev;
        }
#endif
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void LaunchShapeFactories_ZeroOrNegativeInputs_ThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchShape.ForRows(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchShape.ForRows(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchShape.ForRows(64, blockX: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchShapes.TwoDimensional(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchShapes.TwoDimensional(64, blockX: 0, blockY: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchShapes.TwoDimensional(64, blockX: 8, blockY: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchShapes.ThreeDimensional(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchShapes.ThreeDimensional(64, blockX: 0, blockY: 2, blockZ: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchShapes.ThreeDimensional(64, blockX: 4, blockY: 0, blockZ: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchShapes.ThreeDimensional(64, blockX: 4, blockY: 2, blockZ: 0));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LaunchShape64BitTotals_Avoid32BitOverflowAndGuardThreadCount()
    {
        // D1-7a (#805): 64-bit TotalBlocks and TotalThreads must not wrap around 32-bit signed/unsigned integers,
        // while 32-bit ThreadCount must throw OverflowException when TotalThreads > int.MaxValue.
        var largeShape = new LaunchShape(GridX: 65_535, GridY: 65_535, GridZ: 1, BlockX: 32, BlockY: 1, BlockZ: 1, Rows: 64);
        Assert.Equal(65_535UL * 65_535UL, largeShape.TotalBlocks);
        Assert.Equal(65_535UL * 65_535UL * 32UL, largeShape.TotalThreads);
        Assert.Throws<OverflowException>(() => _ = largeShape.ThreadCount);

        // 64-bit GlobalLinearGid calculation on high 3D block coordinates must not overflow 32-bit int.
        var highBlockCtx = new GpuThreadContext(
            ThreadIdxX: 31,
            ThreadIdxY: 0,
            ThreadIdxZ: 0,
            BlockIdxX: 65_534,
            BlockIdxY: 65_534,
            BlockIdxZ: 1,
            BlockDimX: 32,
            BlockDimY: 1,
            BlockDimZ: 1,
            GridDimX: 65_535,
            GridDimY: 65_535,
            GridDimZ: 2);

        long expectedBid = 65_534L + (65_534L * 65_535L) + (1L * 65_535L * 65_535L);
        long expectedGid = (expectedBid * 32L) + 31L;
        Assert.Equal(expectedBid, LaunchShapeIndexMath.GridLinearBid(in highBlockCtx));
        Assert.Equal(expectedGid, LaunchShapeIndexMath.GlobalLinearGid(in highBlockCtx));
    }
}
