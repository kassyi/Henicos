#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.BoundsAndMemoryDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for array and <see cref="Span{T}"/> boundary edges, array indexing bounds,
/// canary index bounds, and <see cref="Span{T}.Slice(int)"/> start and count bounds.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
[Collection(TestCollections.BoundsAndMemoryDifferential)]
public class ArrayAndSpanBoundsDifferentialTests
{
    internal static readonly DifferentialCase s_arrayAndSpanBoundaryEdges = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.ArrayAndSpanBoundaryEdgesKernel)))
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        ExtraShapesSeed0Only =
        [
            LaunchShapes.Linear(64, 32),
            LaunchShapes.IrregularBlock(64, 17),
        ],
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("src", DiffValues.Int32(rng, shape.Rows), readOnly: true)
            .Out<int>(
                "dst",
                shape.Rows,
                BoundsAndMemoryDifferentialKernels.BoundaryEdges4PathColumns,
                OutputPolicy.Exact),
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchArrayAndSpanBoundaryEdgesKernel(
            b.Get<int>("src"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_arrayIndexBounds = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.ArrayIndexBoundsKernel)))
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        Seeds = [0, 1, 2, 3, 4],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (plain array out-of-bounds access terminates process on native)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var src = new int[rows];
            var readIndices = new int[rows];
            var writeIndices = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                src[k] = (k + 1) * 13;
                readIndices[k] = k == 0 ? 0 : (k == rows - 1 ? rows - 1 : ((k * 7) % rows));
                writeIndices[k] = k;
            }

            switch (rng.Seed)
            {
                case 1:
                    readIndices[37 % rows] = -1;
                    break;
                case 2:
                    readIndices[0] = int.MinValue;
                    break;
                case 3:
                    readIndices[rows - 1] = rows;
                    break;
                case 4:
                    writeIndices[15 % rows] = rows;
                    break;
            }

            return new DiffBuffers()
                .In("src", src, readOnly: true)
                .In("readIndices", readIndices, readOnly: true)
                .In("writeIndices", writeIndices, readOnly: true)
                .Out<int>("dst", rows, ["val_plus_len"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchArrayIndexBoundsKernel(
            b.Get<int>("src"),
            b.Get<int>("readIndices"),
            b.Get<int>("writeIndices"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_spanCanaryIndexBounds = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.SpanCanaryIndexBoundsKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4],
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
                    indices[7] = -1;
                    break;
                case 2:
                    indices[19] = -CanaryStart;
                    break;
                case 3:
                    indices[31] = CanaryLen;
                    break;
                case 4:
                    indices[63] = CanaryLen + CanaryStart - 1;
                    break;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["span_sum"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchSpanCanaryIndexBoundsKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_spanSliceStart = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.SpanSliceStartKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var starts = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                starts[k] = (k % 3 == 0) ? 0 : (k % 3 == 1 ? CanaryLen : (k % (CanaryLen / 2)));
            }

            switch (rng.Seed)
            {
                case 1:
                    starts[3] = -1;
                    break;
                case 2:
                    starts[7] = CanaryLen + 1;
                    break;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("starts", starts, readOnly: true)
                .Out<int>("dst", rows, ["slice_res"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchSpanSliceStartKernel(
            b.Get<int>("backing"),
            b.Get<int>("starts"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_spanSliceStartCount = new(
        KernelName(nameof(BoundsAndMemoryDifferentialKernels.SpanSliceStartCountKernel)))
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var starts = new int[rows];
            var counts = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                int kind = k % 4;
                switch (kind)
                {
                    case 0:
                        starts[k] = 0;
                        counts[k] = CanaryLen;
                        break;
                    case 1:
                        starts[k] = CanaryLen;
                        counts[k] = 0;
                        break;
                    case 2:
                        starts[k] = 1;
                        counts[k] = 1;
                        break;
                    default:
                        starts[k] = k % (CanaryLen / 2);
                        counts[k] = (k % (CanaryLen / 2)) + 1;
                        break;
                }
            }

            switch (rng.Seed)
            {
                case 1:
                    counts[3] = -1;
                    break;
                case 2:
                    starts[7] = 1;
                    counts[7] = CanaryLen;
                    break;
                case 3:
                    starts[11] = int.MaxValue;
                    counts[11] = 2;
                    break;
                case 4:
                    starts[15] = 2;
                    counts[15] = int.MaxValue;
                    break;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("starts", starts, readOnly: true)
                .In("counts", counts, readOnly: true)
                .Out<int>("dst", rows, ["slice_res"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchSpanSliceStartCountKernel(
            b.Get<int>("backing"),
            b.Get<int>("starts"),
            b.Get<int>("counts"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    [SkippableFact]
    public void BoundsCheck_ArrayAndSpanEdges_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arrayAndSpanBoundaryEdges);

    [SkippableFact]
    public void ArrayIndex_OutOfBounds_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arrayIndexBounds);

    [SkippableFact]
    public void SpanCanary_IndexBounds_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_spanCanaryIndexBounds);

    [SkippableFact]
    public void SpanSlice_StartIndex_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_spanSliceStart);

    [SkippableFact]
    public void SpanSlice_StartAndCount_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_spanSliceStartCount);
}
