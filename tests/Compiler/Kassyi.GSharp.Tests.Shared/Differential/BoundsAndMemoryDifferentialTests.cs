#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for array, <see cref="Span{T}"/>, <see cref="ReadOnlySpan{T}"/>,
/// <c>Span.Slice</c>, <see cref="ArraySegment{T}"/>, <see cref="Memory{T}"/>, <see cref="SharedMemory{T}"/>,
/// <c>[InlineArray]</c>, null array vs optional null array (#706 / #838), Charter §4 bounds-check over-elimination
/// witnesses, and simultaneous multi-block trap contention.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class BoundsAndMemoryDifferentialTests
{
    private const int DefaultRows = 64;
    private const int SimultaneousRows = 256;
    private const int CanaryStart = BoundsAndMemoryDifferentialKernels.CanaryPad;
    private const int CanaryLen = BoundsAndMemoryDifferentialKernels.CanarySliceLength;
    private const int CanaryTotal = BoundsAndMemoryDifferentialKernels.CanaryBackingLength;
    private const int SharedTileLen = BoundsAndMemoryDifferentialKernels.SharedTileLength;

    private static int[] CreateCanaryBacking()
    {
        var backing = new int[CanaryTotal];
        for (int i = 0; i < CanaryTotal; i++)
        {
            backing[i] = (i < CanaryStart || i >= CanaryStart + CanaryLen)
                ? -10000 - i
                : (i - CanaryStart + 1) * 100;
        }

        return backing;
    }

    private static readonly DifferentialCase s_arrayAndSpanBoundaryEdges = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.ArrayAndSpanBoundaryEdgesKernel)}")
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        ExtraShapesSeed0Only =
        [
            LaunchShapes.Linear(64, 32),
            LaunchShapes.IrregularBlock(64, 17),
        ],
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("src", DiffValues.Int32(rng, shape.Rows), readOnly: true)
            .Out<int>("dst", shape.Rows, BoundsAndMemoryDifferentialKernels.BoundaryEdges4PathColumns, OutputPolicy.Exact),
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchArrayAndSpanBoundaryEdgesKernel(
            b.Get<int>("src"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.ArrayAndSpanBoundaryEdgesKernel(
            b.Get<int>("src"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_arrayIndexBounds = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.ArrayIndexBoundsKernel)}")
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
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.ArrayIndexBoundsKernel(
            b.Get<int>("src"),
            b.Get<int>("readIndices"),
            b.Get<int>("writeIndices"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_spanCanaryIndexBounds = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.SpanCanaryIndexBoundsKernel)}")
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
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.SpanCanaryIndexBoundsKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows),
    };

    private static readonly DifferentialCase s_spanSliceStart = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.SpanSliceStartKernel)}")
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
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.SpanSliceStartKernel(
            b.Get<int>("backing"),
            b.Get<int>("starts"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows),
    };

    private static readonly DifferentialCase s_spanSliceStartCount = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.SpanSliceStartCountKernel)}")
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
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.SpanSliceStartCountKernel(
            b.Get<int>("backing"),
            b.Get<int>("starts"),
            b.Get<int>("counts"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows),
    };

    private static readonly DifferentialCase s_arraySegmentBounds = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.ArraySegmentBoundsKernel)}")
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
        Oracle = static (b, s) =>
        {
            var backing = b.Get<int>("backing");
            BoundsAndMemoryDifferentialKernels.ArraySegmentBoundsKernel(
                new ArraySegment<int>(backing, CanaryStart, CanaryLen),
                b.Get<int>("indices"),
                b.Get<int>("dst"),
                s.Rows);
        },
    };

    private static readonly DifferentialCase s_memoryBounds = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.MemoryBoundsKernel)}")
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
        Oracle = static (b, s) =>
        {
            var backing = b.Get<int>("backing");
            BoundsAndMemoryDifferentialKernels.MemoryBoundsKernel(
                new Memory<int>(backing, CanaryStart, CanaryLen),
                new ReadOnlyMemory<int>(backing, CanaryStart, CanaryLen),
                b.Get<int>("indices"),
                b.Get<int>("dst"),
                s.Rows);
        },
    };

    private static readonly DifferentialCase s_sharedMemoryBounds = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.SharedMemoryBoundsKernel)}")
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

    private static readonly DifferentialCase s_inlineArrayBounds = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.InlineArrayBoundsKernel)}")
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
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.InlineArrayBoundsKernel(
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_nullArrayOptionalValid = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.NullArrayOptionalValidKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("src", DiffValues.Int32(rng, shape.Rows), readOnly: true)
            .Out<int>("dst", shape.Rows, ["opt_val"], OutputPolicy.Exact),
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNullArrayOptionalValidKernel(
            b.Get<int>("src"),
            b.Get<int>("dst"),
            s.Rows,
            opt: null,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.NullArrayOptionalValidKernel(
            b.Get<int>("src"),
            b.Get<int>("dst"),
            s.Rows,
            opt: null),
    };

    private static readonly DifferentialCase s_nullArrayLengthTrap = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.NullArrayLengthTrapKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        ExpectedException = static (_, _) => ExceptionPolicy.For<NullReferenceException>(DiffPath.Managed | DiffPath.Native),
        Inputs = static (_, shape) => new DiffBuffers()
            .Out<int>("dst", shape.Rows, ["trap_val"], OutputPolicy.Exact),
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNullArrayLengthTrapKernel(
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.NullArrayLengthTrapKernel(
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_nullArrayIndexerTrap = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.NullArrayIndexerTrapKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        ExpectedException = static (_, _) => ExceptionPolicy.For<NullReferenceException>(DiffPath.Managed | DiffPath.Native),
        Inputs = static (_, shape) => new DiffBuffers()
            .Out<int>("dst", shape.Rows, ["trap_val"], OutputPolicy.Exact),
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNullArrayIndexerTrapKernel(
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.NullArrayIndexerTrapKernel(
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_nullArrayForeachTrap = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.NullArrayForeachTrapKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        ExpectedException = static (_, _) => ExceptionPolicy.For<NullReferenceException>(DiffPath.Managed | DiffPath.Native),
        Inputs = static (_, shape) => new DiffBuffers()
            .Out<int>("dst", shape.Rows, ["trap_val"], OutputPolicy.Exact),
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNullArrayForeachTrapKernel(
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.NullArrayForeachTrapKernel(
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_nullArrayReassignedParameterTrap = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.NullArrayReassignedParameterTrapKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        ExpectedException = static (_, _) => ExceptionPolicy.For<NullReferenceException>(DiffPath.Managed | DiffPath.Native),
        Inputs = static (rng, shape) => new DiffBuffers()
            .In("src", DiffValues.Int32(rng, shape.Rows), readOnly: true)
            .Out<int>("dst", shape.Rows, ["trap_val"], OutputPolicy.Exact),
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNullArrayReassignedParameterTrapKernel(
            b.Get<int>("src"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.NullArrayReassignedParameterTrapKernel(
            b.Get<int>("src"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_boundsCheckEliminationValid = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationValidKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Inputs = static (_, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var indices = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                indices[k] = k % CanaryLen;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["bce_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchBoundsCheckEliminationValidKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationValidKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows),
    };

    private static readonly DifferentialCase s_boundsCheckEliminationOffByOne = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationOffByOneKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
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

            if (rng.Seed == 1)
            {
                indices[1] = CanaryLen;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["bce_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchBoundsCheckEliminationOffByOneKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationOffByOneKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows),
    };

    private static readonly DifferentialCase s_boundsCheckEliminationWrongSpan = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationWrongSpanKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var otherBacking = CreateCanaryBacking();
            var indices = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                indices[k] = k % CanaryLen;
            }

            if (rng.Seed == 1)
            {
                indices[2] = CanaryLen;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("otherBacking", otherBacking, readOnly: true)
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["bce_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchBoundsCheckEliminationWrongSpanKernel(
            b.Get<int>("backing"),
            b.Get<int>("otherBacking"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            CanaryLen + 4,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationWrongSpanKernel(
            b.Get<int>("backing"),
            b.Get<int>("otherBacking"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            CanaryLen + 4,
            s.Rows),
    };

    private static readonly DifferentialCase s_boundsCheckEliminationPostIncrement = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationPostIncrementKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var indices = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                indices[k] = k % (CanaryLen - 1);
            }

            if (rng.Seed == 1)
            {
                indices[3] = CanaryLen - 1;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["bce_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchBoundsCheckEliminationPostIncrementKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationPostIncrementKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows),
    };

    private static readonly DifferentialCase s_boundsCheckEliminationUpperOnly = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationUpperOnlyKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
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

            if (rng.Seed == 1)
            {
                indices[4] = -1;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("indices", indices, readOnly: true)
                .Out<int>("dst", rows, ["bce_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchBoundsCheckEliminationUpperOnlyKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationUpperOnlyKernel(
            b.Get<int>("backing"),
            b.Get<int>("indices"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows),
    };

    private static readonly DifferentialCase s_allThreadsNullDereferenceTrap = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.AllThreadsNullDereferenceTrapKernel)}")
    {
        Shape = new LaunchShape(2, 2, 2, 4, 4, 2, SimultaneousRows),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var flags = new int[rows];
            if (rng.Seed == 1)
            {
                Array.Fill(flags, 1);
            }

            return new DiffBuffers()
                .In("flags", flags, readOnly: true)
                .Out<int>("dst", rows, ["trap_res"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchAllThreadsNullDereferenceTrapKernel(
            b.Get<int>("flags"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            gridDimY: s.GridY,
            gridDimZ: s.GridZ,
            blockDimX: s.BlockX,
            blockDimY: s.BlockY,
            blockDimZ: s.BlockZ),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.AllThreadsNullDereferenceTrapKernel(
            b.Get<int>("flags"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_allThreadsEmptyFirstTrap = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.AllThreadsEmptyFirstTrapKernel)}")
    {
        Shape = new LaunchShape(2, 2, 2, 4, 4, 2, SimultaneousRows),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var emptyArr = new int[4];
            var flags = new int[rows];
            if (rng.Seed == 1)
            {
                Array.Fill(flags, 1);
            }

            return new DiffBuffers()
                .In("emptyArr", emptyArr, readOnly: true)
                .In("flags", flags, readOnly: true)
                .Out<int>("dst", rows, ["trap_res"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchAllThreadsEmptyFirstTrapKernel(
            b.Get<int>("emptyArr"),
            b.Get<int>("flags"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            gridDimY: s.GridY,
            gridDimZ: s.GridZ,
            blockDimX: s.BlockX,
            blockDimY: s.BlockY,
            blockDimZ: s.BlockZ),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.AllThreadsEmptyFirstTrapKernel(
            b.Get<int>("emptyArr"),
            b.Get<int>("flags"),
            b.Get<int>("dst"),
            s.Rows),
    };

    [SkippableFact]
    public void Test_ArrayAndSpanBoundaryEdges_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arrayAndSpanBoundaryEdges);

    [SkippableFact]
    public void Test_ArrayIndexBounds_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arrayIndexBounds);

    [SkippableFact]
    public void Test_SpanCanaryIndexBounds_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_spanCanaryIndexBounds);

    [SkippableFact]
    public void Test_SpanSliceStart_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_spanSliceStart);

    [SkippableFact]
    public void Test_SpanSliceStartCount_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_spanSliceStartCount);

    [SkippableFact]
    public void Test_ArraySegmentBounds_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arraySegmentBounds);

    [SkippableFact]
    public void Test_MemoryBounds_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_memoryBounds);

    [SkippableFact]
    public void Test_SharedMemoryBounds_MatchesTheReferenceAndTrapsOnManaged() =>
        DifferentialRunner.Run(s_sharedMemoryBounds);

    [SkippableFact]
    public void Test_InlineArrayBounds_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_inlineArrayBounds);

    [SkippableFact]
    public void Test_NullArrayOptionalValid_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayOptionalValid);

    [SkippableFact]
    public void Test_NullArrayLengthTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayLengthTrap);

    [SkippableFact]
    public void Test_NullArrayIndexerTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayIndexerTrap);

    [SkippableFact]
    public void Test_NullArrayForeachTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayForeachTrap);

    [SkippableFact]
    public void Test_NullArrayReassignedParameterTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayReassignedParameterTrap);

    [SkippableFact]
    public void Test_BoundsCheckEliminationValid_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_boundsCheckEliminationValid);

    [SkippableFact]
    public void Test_BoundsCheckEliminationOffByOne_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_boundsCheckEliminationOffByOne);

    [SkippableFact]
    public void Test_BoundsCheckEliminationWrongSpan_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_boundsCheckEliminationWrongSpan);

    [SkippableFact]
    public void Test_BoundsCheckEliminationPostIncrement_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_boundsCheckEliminationPostIncrement);

    [SkippableFact]
    public void Test_BoundsCheckEliminationUpperOnly_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_boundsCheckEliminationUpperOnly);

    [SkippableFact]
    public void Test_AllThreadsNullDereferenceTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_allThreadsNullDereferenceTrap);

    [SkippableFact]
    public void Test_AllThreadsEmptyFirstTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_allThreadsEmptyFirstTrap);
}
