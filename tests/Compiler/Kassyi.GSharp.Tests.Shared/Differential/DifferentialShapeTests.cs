#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public class DifferentialShapeTests
{
    // ---- LaunchShape and ThreadOrder ----

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void LaunchShape_ContextOf_NumbersThreadsLikeTheManagedLauncher()
    {
        // ForRows rounds the grid up: 441 rows in blocks of 64 take 7 blocks
        // mutant: LaunchShape.cs ForRows `+ blockX - 1` removed
        var rows = LaunchShape.ForRows(441, blockX: 64);
        Assert.Equal((7u, 448), (rows.GridX, rows.ThreadCount));

        // x fastest inside the block, then y, then z; blocks in the same order
        // mutant: LaunchShape.cs ContextOf `thread % bx, thread / bx % by` -> `thread / by % bx, thread % by`
        var shape = new LaunchShape(GridX: 2, GridY: 2, GridZ: 2, BlockX: 4, BlockY: 2, BlockZ: 2, Rows: 128);
        var context = shape.ContextOf((5 * 16) + (1 * 8) + (1 * 4) + 3);
        Assert.Equal(new GpuThreadContext(3, 1, 1, 1, 0, 1, 4, 2, 2, 2, 2, 2), context);
        Assert.Equal(new GpuThreadContext(2, 0, 1, 1, 0, 1, 4, 2, 2, 2, 2, 2), shape.ContextOf((5 * 16) + (1 * 8) + (0 * 4) + 2));
        Assert.Equal(new GpuThreadContext(0, 0, 0, 0, 0, 0, 4, 2, 2, 2, 2, 2), shape.ContextOf(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => shape.ContextOf(128));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void LaunchShapes_TwoDimensional_NonDegenerateAt64()
    {
        // Both TwoDimensional(64) and ThreeDimensional(64) must keep GridY >= 2 (and GridZ >= 2 in 3D) while covering all 64 rows without duplicate linear IDs
        // mutant: LaunchShapes.cs TwoDimensional `Math.Max(2u, ...)` -> `Math.Max(1u, ...)`
        var twoDim = LaunchShapes.TwoDimensional(64);
        Assert.True(twoDim.GridY >= 2, $"Expected GridY >= 2 for 2D shape at 64 rows, got {twoDim}");
        Assert.True(twoDim.BlockY >= 2, $"Expected BlockY >= 2 for 2D shape at 64 rows, got {twoDim}");
        Assert.True(twoDim.ThreadCount >= 64);

        var threeDim = LaunchShapes.ThreeDimensional(64);
        Assert.True(threeDim.GridY >= 2, $"Expected GridY >= 2 for 3D shape at 64 rows, got {threeDim}");
        Assert.True(threeDim.GridZ >= 2, $"Expected GridZ >= 2 for 3D shape at 64 rows, got {threeDim}");
        Assert.True(threeDim.BlockY >= 2 && threeDim.BlockZ >= 2);
        Assert.True(threeDim.ThreadCount >= 64);

        var seenIds = new HashSet<int>();
        for (int id = 0; id < 64; id++)
        {
            var ctx = threeDim.ContextOf(id);
            int tid = ctx.ThreadIdxX + (ctx.ThreadIdxY * ctx.BlockDimX) + (ctx.ThreadIdxZ * ctx.BlockDimX * ctx.BlockDimY);
            int bid = ctx.BlockIdxX + (ctx.BlockIdxY * ctx.GridDimX) + (ctx.BlockIdxZ * ctx.GridDimX * ctx.GridDimY);
            int reconstructed = (bid * threeDim.ThreadsPerBlock) + tid;
            Assert.Equal(id, reconstructed);
            Assert.True(seenIds.Add(reconstructed));
        }

        Assert.Equal(64, seenIds.Count);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void ThreadOrder_ForwardAndReverse_EnumeratesEveryThreadOnce()
    {
        var shape = LaunchShape.ForRows(64, blockX: 16);
        int[] forward = Enumerable.Range(0, 64).ToArray();

        Assert.Equal(forward, ThreadOrder.Forward.Enumerate(shape));
        Assert.Equal(forward.Reverse(), ThreadOrder.Reverse.Enumerate(shape));

        // A permutation: every id once, not the forward order, and the same for the same seed
        // mutant: ThreadOrder.cs Enumerate `int j = rng.NextBelow(i + 1);` -> `int j = i;`
        var permuted = ThreadOrder.Permutation(5).Enumerate(shape);
        Assert.Equal(forward, permuted.Order());
        Assert.NotEqual(forward, permuted);
        Assert.Equal(permuted, ThreadOrder.Permutation(5).Enumerate(shape));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
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
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void LaunchShape64BitTotals_Avoid32BitOverflowAndGuardThreadCount()
    {
        // 64-bit TotalBlocks and TotalThreads must not wrap around 32-bit signed/unsigned integers,
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
