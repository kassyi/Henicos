#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for Lane D5 Stage 1: array, <see cref="Span{T}"/>, <see cref="ReadOnlySpan{T}"/>,
/// <c>Span.Slice</c>, <see cref="ArraySegment{T}"/>, <see cref="Memory{T}"/>, <see cref="SharedMemory{T}"/>,
/// <c>[InlineArray]</c>, null array vs optional null array (#706 / #838), Charter §4 bounds-check over-elimination
/// witnesses, and simultaneous multi-block trap contention.
/// </summary>
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

    // B-01: 1D array indexing bounds (Seed 0 in-bounds across all 4 paths; Seeds 1..4 OOB on Oracle vs Managed).
    private static readonly DifferentialCase s_arrayIndexBounds = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.ArrayIndexBoundsKernel)}")
    {
        Shape = LaunchShapes.PartialWarp(74, 37),
        Seeds = [0, 1, 2, 3, 4],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed,
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

    // B-02: Span<T> / ReadOnlySpan<T> indexing over a canary-padded backing array (#718).
    private static readonly DifferentialCase s_spanCanaryIndexBounds = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.SpanCanaryIndexBoundsKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed | DiffPath.Native,
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

    // B-03: Span.Slice(start) and Span.Slice(start, count) bounds and 32-bit start + count overflow (#718).
    private static readonly DifferentialCase s_spanSliceBoundsAndOverflow = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.SpanSliceBoundsAndOverflowKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4, 5, 6],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed | DiffPath.Native,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var starts = new int[rows];
            var counts = new int[rows];
            var modes = new int[rows];

            for (int k = 0; k < rows; k++)
            {
                int kind = k % 6;
                switch (kind)
                {
                    case 0:
                        modes[k] = 0;
                        starts[k] = 0;
                        counts[k] = CanaryLen;
                        break;
                    case 1:
                        // Valid empty slice at the exact end of the span: Slice(Length) and Slice(Length, 0).
                        modes[k] = 0;
                        starts[k] = CanaryLen;
                        counts[k] = 0;
                        break;
                    case 2:
                        modes[k] = 1;
                        starts[k] = CanaryLen;
                        counts[k] = 0;
                        break;
                    case 3:
                        modes[k] = 2;
                        starts[k] = 0;
                        counts[k] = CanaryLen;
                        break;
                    case 4:
                        modes[k] = 2;
                        starts[k] = CanaryLen - 1;
                        counts[k] = 1;
                        break;
                    default:
                        modes[k] = 2;
                        starts[k] = k % (CanaryLen / 2);
                        counts[k] = (k % (CanaryLen / 2)) + 1;
                        break;
                }
            }

            switch (rng.Seed)
            {
                case 1:
                    modes[3] = 0;
                    starts[3] = -1;
                    break;
                case 2:
                    modes[7] = 0;
                    starts[7] = CanaryLen + 1;
                    break;
                case 3:
                    modes[11] = 1;
                    starts[11] = 0;
                    counts[11] = -1;
                    break;
                case 4:
                    modes[15] = 1;
                    starts[15] = 1;
                    counts[15] = CanaryLen;
                    break;
                case 5:
                    // 32-bit signed start + count wrap-around: int.MaxValue + 2 < 0 (#718).
                    modes[19] = 1;
                    starts[19] = int.MaxValue;
                    counts[19] = 2;
                    break;
                case 6:
                    modes[23] = 1;
                    starts[23] = 2;
                    counts[23] = int.MaxValue;
                    break;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("starts", starts, readOnly: true)
                .In("counts", counts, readOnly: true)
                .In("modes", modes, readOnly: true)
                .Out<int>("dst", rows, ["slice_res"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchSpanSliceBoundsAndOverflowKernel(
            b.Get<int>("backing"),
            b.Get<int>("starts"),
            b.Get<int>("counts"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.SpanSliceBoundsAndOverflowKernel(
            b.Get<int>("backing"),
            b.Get<int>("starts"),
            b.Get<int>("counts"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            s.Rows),
    };

    // B-04: ArraySegment<T> (ArgumentOutOfRangeException, S33) and Memory<T> (IndexOutOfRangeException) (#718).
    private static readonly DifferentialCase s_arraySegmentAndMemoryBounds = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.ArraySegmentAndMemoryBoundsKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed | DiffPath.Native,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var indices = new int[rows];
            var modes = new int[rows];
            for (int k = 0; k < rows; k++)
            {
                indices[k] = k % CanaryLen;
                modes[k] = k & 1;
            }

            switch (rng.Seed)
            {
                case 1:
                    modes[5] = 0;
                    indices[5] = -1;
                    break;
                case 2:
                    modes[9] = 0;
                    indices[9] = CanaryLen;
                    break;
                case 3:
                    modes[12] = 1;
                    indices[12] = -1;
                    break;
                case 4:
                    modes[20] = 1;
                    indices[20] = CanaryLen;
                    break;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("indices", indices, readOnly: true)
                .In("modes", modes, readOnly: true)
                .Out<int>("dst", rows, ["seg_or_mem_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) =>
        {
            var backing = b.Get<int>("backing");
            BoundsAndMemoryDifferentialKernels.LaunchArraySegmentAndMemoryBoundsKernel(
                new ArraySegment<int>(backing, CanaryStart, CanaryLen),
                new Memory<int>(backing, CanaryStart, CanaryLen),
                new ReadOnlyMemory<int>(backing, CanaryStart, CanaryLen),
                b.Get<int>("indices"),
                b.Get<int>("modes"),
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
        Oracle = static (b, s) =>
        {
            var backing = b.Get<int>("backing");
            BoundsAndMemoryDifferentialKernels.ArraySegmentAndMemoryBoundsKernel(
                new ArraySegment<int>(backing, CanaryStart, CanaryLen),
                new Memory<int>(backing, CanaryStart, CanaryLen),
                new ReadOnlyMemory<int>(backing, CanaryStart, CanaryLen),
                b.Get<int>("indices"),
                b.Get<int>("modes"),
                b.Get<int>("dst"),
                s.Rows);
        },
    };

    // B-05: Static SharedMemory<T> bounds (Seed 0 across Managed/Native/Cuda; Seeds 1..4 OOB on Managed).
    private static readonly DifferentialCase s_sharedMemoryBounds = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.SharedMemoryBoundsKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4],
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

    // B-06: [InlineArray(4)] indexing bounds over canary-padded struct holder (#718).
    private static readonly DifferentialCase s_inlineArrayBounds = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.InlineArrayBoundsKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed | DiffPath.Native,
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

    // B-07: Null local/reassigned array dereference (NullReferenceException on BOTH Managed and Native, #706)
    // vs optional null array parameter (#838).
    private static readonly DifferentialCase s_nullArrayVsOptionalNullArray = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.NullArrayVsOptionalNullArrayKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed | DiffPath.Native,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var src = DiffValues.Int32(rng, rows);
            var modes = new int[rows];

            switch (rng.Seed)
            {
                case 1:
                    modes[3] = 1;
                    break;
                case 2:
                    modes[7] = 2;
                    break;
                case 3:
                    modes[11] = 3;
                    break;
                case 4:
                    modes[15] = 4;
                    break;
            }

            return new DiffBuffers()
                .In("src", src, readOnly: true)
                .In("modes", modes, readOnly: true)
                .Out<int>("dst", rows, ["null_or_opt_res"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchNullArrayVsOptionalNullArrayKernel(
            b.Get<int>("src"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            s.Rows,
            opt: null,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.NullArrayVsOptionalNullArrayKernel(
            b.Get<int>("src"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            s.Rows,
            opt: null),
    };

    // B-08: Charter §4 bounds-check over-elimination witnesses (#718).
    private static readonly DifferentialCase s_boundsCheckEliminationWitness = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationWitnessKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1, 2, 3, 4],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed | DiffPath.Native,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = CreateCanaryBacking();
            var otherBacking = CreateCanaryBacking();
            var indices = new int[rows];
            var modes = new int[rows];

            for (int k = 0; k < rows; k++)
            {
                modes[k] = k % 5;
                // Keep Seed 0 strictly in-bounds even for mode 3 (which increments i before indexing).
                indices[k] = modes[k] == 3
                    ? (k % (CanaryLen - 1))
                    : (k % CanaryLen);
            }

            switch (rng.Seed)
            {
                case 1:
                    // Off-by-one guard: i <= s.Length with i == s.Length.
                    modes[1] = 1;
                    indices[1] = CanaryLen;
                    break;
                case 2:
                    // Wrong-span guard: (uint)i < (uint)other.Length with i == s.Length < other.Length.
                    modes[2] = 2;
                    indices[2] = CanaryLen;
                    break;
                case 3:
                    // Post-guard increment: (uint)i < (uint)s.Length followed by i++ with i == s.Length - 1.
                    modes[3] = 3;
                    indices[3] = CanaryLen - 1;
                    break;
                case 4:
                    // Signed upper-bound-only guard: i < s.Length with i == -1.
                    modes[4] = 4;
                    indices[4] = -1;
                    break;
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("otherBacking", otherBacking, readOnly: true)
                .In("indices", indices, readOnly: true)
                .In("modes", modes, readOnly: true)
                .Out<int>("dst", rows, ["bce_val"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchBoundsCheckEliminationWitnessKernel(
            b.Get<int>("backing"),
            b.Get<int>("otherBacking"),
            b.Get<int>("indices"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            CanaryLen + 4,
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.BoundsCheckEliminationWitnessKernel(
            b.Get<int>("backing"),
            b.Get<int>("otherBacking"),
            b.Get<int>("indices"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            CanaryStart,
            CanaryLen,
            CanaryLen + 4,
            s.Rows),
    };

    // B-10: All 256 threads in a 3D grid (2,2,2)x(4,4,2) simultaneously trapping on NullReferenceException (Seed 1)
    // and InvalidOperationException (Seed 2) across both Managed and Native.
    private static readonly DifferentialCase s_allThreadsSimultaneousTrap = new(
        $"{nameof(BoundsAndMemoryDifferentialKernels)}.{nameof(BoundsAndMemoryDifferentialKernels.AllThreadsSimultaneousTrapKernel)}")
    {
        Shape = new LaunchShape(2, 2, 2, 4, 4, 2, SimultaneousRows),
        Seeds = [0, 1, 2],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        ExceptionPaths = DiffPath.Managed | DiffPath.Native,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var emptyArr = new int[4];
            var modes = new int[rows];
            if (rng.Seed == 1)
            {
                Array.Fill(modes, 1);
            }
            else if (rng.Seed == 2)
            {
                Array.Fill(modes, 2);
            }

            return new DiffBuffers()
                .In("emptyArr", emptyArr, readOnly: true)
                .In("modes", modes, readOnly: true)
                .Out<int>("dst", rows, ["trap_res"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BoundsAndMemoryDifferentialKernels.LaunchAllThreadsSimultaneousTrapKernel(
            b.Get<int>("emptyArr"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            gridDimY: s.GridY,
            gridDimZ: s.GridZ,
            blockDimX: s.BlockX,
            blockDimY: s.BlockY,
            blockDimZ: s.BlockZ),
        Oracle = static (b, s) => BoundsAndMemoryDifferentialKernels.AllThreadsSimultaneousTrapKernel(
            b.Get<int>("emptyArr"),
            b.Get<int>("modes"),
            b.Get<int>("dst"),
            s.Rows),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ArrayAndSpanBoundaryEdges_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arrayAndSpanBoundaryEdges);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ArrayIndexBounds_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arrayIndexBounds);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SpanCanaryIndexBounds_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_spanCanaryIndexBounds);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SpanSliceBoundsAndOverflow_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_spanSliceBoundsAndOverflow);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ArraySegmentAndMemoryBounds_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arraySegmentAndMemoryBounds);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SharedMemoryBounds_MatchesTheReferenceAndTrapsOnManaged() =>
        DifferentialRunner.Run(s_sharedMemoryBounds);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_InlineArrayBounds_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_inlineArrayBounds);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullArrayVsOptionalNullArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullArrayVsOptionalNullArray);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_BoundsCheckEliminationWitness_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_boundsCheckEliminationWitness);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_AllThreadsSimultaneousTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_allThreadsSimultaneousTrap);
}
