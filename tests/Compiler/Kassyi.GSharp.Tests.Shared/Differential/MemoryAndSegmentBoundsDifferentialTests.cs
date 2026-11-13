#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.BoundsAndMemoryDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for <see cref="ArraySegment{T}"/>, <see cref="Memory{T}"/>,
/// <see cref="SharedMemory{T}"/>, and <c>[InlineArray]</c> bounds.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.BoundsAndMemoryDifferential)]
public class MemoryAndSegmentBoundsDifferentialTests
{
    internal static readonly DifferentialCase s_arraySegmentBounds = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.ArraySegmentBoundsKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var indices = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                indices[k] = k % CanaryLen;
            }

            switch (rng.Seed)
            {
                case 1:
                    indices[5] = -1;
                    break;
                case 2:
                    indices[9] = CanaryLen;
                    break;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["seg_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) =>
        {
            var backing = b.Get<int>("backing");
            BoundsAndMemoryDifferentialKernels.LaunchArraySegmentBoundsKernel(
                new ArraySegment<int>(backing, CanaryStart, CanaryLen),
                b.Get<int>("indices"),
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_memoryBounds = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.MemoryBoundsKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var indices = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                indices[k] = k % CanaryLen;
            }

            switch (rng.Seed)
            {
                case 1:
                    indices[5] = -1;
                    break;
                case 2:
                    indices[9] = CanaryLen;
                    break;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["mem_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) =>
        {
            var backing = b.Get<int>("backing");
            BoundsAndMemoryDifferentialKernels.LaunchMemoryBoundsKernel(
                new Memory<int>(backing, CanaryStart, CanaryLen),
                new ReadOnlyMemory<int>(backing, CanaryStart, CanaryLen),
                b.Get<int>("indices"),
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_sharedMemoryBounds = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.SharedMemoryBoundsKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4],
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (shared memory out-of-bounds access terminates process on native)",
        },
        ExpectedException = static (rng, _) => rng.Seed == 0
            ? null
            : ExceptionPolicy.For<IndexOutOfRangeException>(DiffPath.Managed),
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var indices = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                indices[k] = k % SharedTileLen;
            }

            switch (rng.Seed)
            {
                case 1:
                    indices[0] = -1;
                    break;
                case 2:
                    indices[15] = SharedTileLen;
                    break;
                case 3:
                    indices[31] = SharedTileLen + 1;
                    break;
                case 4:
                    indices[63] = int.MinValue;
                    break;
            }

            return new DiffBuffers()
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["smem_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchSharedMemoryBoundsKernel(
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, s) =>
        {
            var indices = b.Get<int>("indices");
            var dst = b.Get<int>("dst");
            for (int t = 0; t < s.Rows; t++)
            {
                dst[t] = (indices[t] + 1) * 10;
            }
        },
    };

    internal static readonly DifferentialCase s_inlineArrayBounds = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.InlineArrayBoundsKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var indices = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                indices[k] = k & 3;
            }

            switch (rng.Seed)
            {
                case 1:
                    indices[4] = -1;
                    break;
                case 2:
                    indices[8] = -4;
                    break;
                case 3:
                    indices[12] = 4;
                    break;
                case 4:
                    indices[16] = 7;
                    break;
            }

            return new DiffBuffers()
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["inline_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchInlineArrayBoundsKernel(
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    public void BoundsCheck_ArraySegment_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arraySegmentBounds);

    [SkippableFact]
    public void BoundsCheck_MemorySegment_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_memoryBounds);

    [SkippableFact]
    public void BoundsCheck_SharedMemory_MatchesTheReferenceAndTrapsOnManaged() =>
        DifferentialRunner.Run(s_sharedMemoryBounds);

    [SkippableFact]
    public void BoundsCheck_InlineArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_inlineArrayBounds);
}
