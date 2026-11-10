#nullable enable
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Tests.Infrastructure;
using Kassyi.ZeroAlloc.Linq;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for runtime checks and BCL exceptions: <see cref="Nullable{T}.Value"/> (<see cref="InvalidOperationException"/>,
/// #718), <c>Math.Clamp</c> (<see cref="ArgumentException"/> when <c>min &gt; max</c>, #718), <c>StackLinq</c> empty and
/// multi-element sequence traps (<see cref="InvalidOperationException"/> and <see cref="ArgumentOutOfRangeException"/>),
/// <c>ElementAtOrDefault(-1)</c> non-Span enumeration side effects (#1060), <c>MemoryExtensions.Sort</c> length mismatch
/// and comparer exception wrapping, and post-trap non-continuation before <c>100 / denom</c> (#729).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public sealed class BclExceptionDifferentialTests
{
    private const int DefaultRows = 32;
    private const int SliceWidth = BclExceptionDifferentialKernels.SliceWidth;

    private static readonly (double Val, double Min, double Max)[] s_clampBoundaryTriples =
    [
        (0.5, 0.0, 1.0),
        (2.0, 0.0, 1.0),
        (-1.0, 0.0, 1.0),
        (double.NaN, 0.0, 1.0),
        (-0.0, 0.0, 1.0),
        (0.0, -0.0, 1.0),
        (0.5, double.NaN, 1.0),
        (0.5, 0.0, double.NaN),
        (1.0, 0.0, 1.0),
        (0.0, 0.0, 1.0),
    ];

    private static readonly DifferentialCase s_nullableInt32ValueAccess = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.NullableInt32ValueAccessKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var hasInt = new int[rows];
            var intVals = DiffValues.Int32(rng, rows);
            Array.Fill(hasInt, 1);

            if (rng.Seed == 1)
            {
                hasInt[5] = 0;
            }

            return new DiffBuffers()
                .In("hasInt", hasInt, readOnly: true)
                .In("intVals", intVals, readOnly: true)
                .Out<int>("dst", rows, BclExceptionDifferentialKernels.NullableInt32ValueColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchNullableInt32ValueAccessKernel(
            b.Get<int>("hasInt"),
            b.Get<int>("intVals"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.NullableInt32ValueAccessKernel(
            b.Get<int>("hasInt"),
            b.Get<int>("intVals"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_nullableStructValueAccess = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.NullableStructValueAccessKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var hasPair = new int[rows];
            var pairX = DiffValues.Int32(rng, rows);
            var pairY = DiffValues.Int32(rng, rows);
            Array.Fill(hasPair, 1);

            if (rng.Seed == 1)
            {
                hasPair[13] = 0;
            }

            return new DiffBuffers()
                .In("hasPair", hasPair, readOnly: true)
                .In("pairX", pairX, readOnly: true)
                .In("pairY", pairY, readOnly: true)
                .Out<int>("dst", rows, BclExceptionDifferentialKernels.NullableStructValueColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchNullableStructValueAccessKernel(
            b.Get<int>("hasPair"),
            b.Get<int>("pairX"),
            b.Get<int>("pairY"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.NullableStructValueAccessKernel(
            b.Get<int>("hasPair"),
            b.Get<int>("pairX"),
            b.Get<int>("pairY"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_mathClampInt32 = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.MathClampInt32Kernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var vals = new int[rows];
            var mins = new int[rows];
            var maxs = new int[rows];

            for (int k = 0; k < rows; k++)
            {
                vals[k] = rng.NextBelow(201) - 100;
                mins[k] = -50;
                maxs[k] = 50;
            }

            if (rng.Seed == 1)
            {
                mins[3] = 10;
                maxs[3] = 5; // min > max throws ArgumentException
            }

            return new DiffBuffers()
                .In("vals", vals, readOnly: true)
                .In("mins", mins, readOnly: true)
                .In("maxs", maxs, readOnly: true)
                .Out<int>("dst", rows, BclExceptionDifferentialKernels.MathClampIntColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchMathClampInt32Kernel(
            b.Get<int>("vals"),
            b.Get<int>("mins"),
            b.Get<int>("maxs"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.MathClampInt32Kernel(
            b.Get<int>("vals"),
            b.Get<int>("mins"),
            b.Get<int>("maxs"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_mathClampSingle = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.MathClampSingleKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var vals = new float[rows];
            var mins = new float[rows];
            var maxs = new float[rows];

            for (int k = 0; k < rows; k++)
            {
                var (v, min, max) = s_clampBoundaryTriples[k % s_clampBoundaryTriples.Length];
                vals[k] = (float)v;
                mins[k] = (float)min;
                maxs[k] = (float)max;
            }

            if (rng.Seed == 1)
            {
                mins[7] = 2.0f;
                maxs[7] = 1.0f; // min > max throws ArgumentException
            }

            return new DiffBuffers()
                .In("vals", vals, readOnly: true)
                .In("mins", mins, readOnly: true)
                .In("maxs", maxs, readOnly: true)
                .Out<float>("dst", rows, BclExceptionDifferentialKernels.MathClampFloatColumns, OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchMathClampSingleKernel(
            b.Get<float>("vals"),
            b.Get<float>("mins"),
            b.Get<float>("maxs"),
            b.Get<float>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.MathClampSingleKernel(
            b.Get<float>("vals"),
            b.Get<float>("mins"),
            b.Get<float>("maxs"),
            b.Get<float>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_mathClampDouble = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.MathClampDoubleKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var vals = new double[rows];
            var mins = new double[rows];
            var maxs = new double[rows];

            for (int k = 0; k < rows; k++)
            {
                var (v, min, max) = s_clampBoundaryTriples[k % s_clampBoundaryTriples.Length];
                vals[k] = v;
                mins[k] = min;
                maxs[k] = max;
            }

            if (rng.Seed == 1)
            {
                mins[7] = 2.0;
                maxs[7] = 1.0; // min > max throws ArgumentException
            }

            return new DiffBuffers()
                .In("vals", vals, readOnly: true)
                .In("mins", mins, readOnly: true)
                .In("maxs", maxs, readOnly: true)
                .Out<double>("dst", rows, BclExceptionDifferentialKernels.MathClampDoubleColumns, OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchMathClampDoubleKernel(
            b.Get<double>("vals"),
            b.Get<double>("mins"),
            b.Get<double>("maxs"),
            b.Get<double>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.MathClampDoubleKernel(
            b.Get<double>("vals"),
            b.Get<double>("mins"),
            b.Get<double>("maxs"),
            b.Get<double>("dst"),
            s.Rows),
    };

    private delegate void SliceKernel(int[] backing, int[] lengths, int[] dst, int rows);
    private delegate void SliceLaunch(int[] backing, int[] lengths, int[] dst, LaunchShape shape);

    private static DiffBuffers SliceInputs(int rows, int validLength, int faultLength)
    {
        var backing = new int[rows * SliceWidth];
        var lengths = new int[rows];
        for (int k = 0; k < rows; k++)
        {
            lengths[k] = validLength;
            for (int j = 0; j < SliceWidth; j++)
            {
                backing[(k * SliceWidth) + j] = (k * 10) + j + 1;
            }
        }

        lengths[3] = faultLength;

        return new DiffBuffers()
            .In("backing", backing, readOnly: true)
            .In("lengths", lengths, readOnly: true)
            .Out<int>("dst", rows, BclExceptionDifferentialKernels.StackLinqTrapColumns, OutputPolicy.Exact);
    }

    private static DifferentialCase SliceCase(
        string kernel, int validLength, int faultLength, SliceLaunch launch, SliceKernel oracle) =>
        new($"{nameof(BclExceptionDifferentialKernels)}.{kernel}")
        {
            Shape = LaunchShapes.Linear(DefaultRows, 32),
            Seeds = [0, 1],
            OracleExceptions = OracleExceptionMode.MatchOracle,
            Inputs = (rng, shape) => SliceInputs(shape.Rows, validLength, rng.Seed == 1 ? faultLength : validLength),
            Launch = (b, s) => launch(b.Get<int>("backing"), b.Get<int>("lengths"), b.Get<int>("dst"), s),
            Oracle = (b, s) => oracle(b.Get<int>("backing"), b.Get<int>("lengths"), b.Get<int>("dst"), s.Rows),
        };

    private static readonly DifferentialCase s_firstOnEmpty = SliceCase(
        nameof(BclExceptionDifferentialKernels.StackLinqFirstKernel),
        validLength: SliceWidth,
        faultLength: 0,
        static (x, n, d, s) => BclExceptionDifferentialKernels.LaunchStackLinqFirstKernel(
            x, n, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX),
        BclExceptionDifferentialKernels.StackLinqFirstKernel);

    private static readonly DifferentialCase s_lastOnEmpty = SliceCase(
        nameof(BclExceptionDifferentialKernels.StackLinqLastKernel),
        validLength: SliceWidth,
        faultLength: 0,
        static (x, n, d, s) => BclExceptionDifferentialKernels.LaunchStackLinqLastKernel(
            x, n, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX),
        BclExceptionDifferentialKernels.StackLinqLastKernel);

    private static readonly DifferentialCase s_singleOnEmpty = SliceCase(
        nameof(BclExceptionDifferentialKernels.StackLinqSingleKernel),
        validLength: 1,
        faultLength: 0,
        static (x, n, d, s) => BclExceptionDifferentialKernels.LaunchStackLinqSingleKernel(
            x, n, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX),
        BclExceptionDifferentialKernels.StackLinqSingleKernel);

    private static readonly DifferentialCase s_singleOnTwoElements = SliceCase(
        $"{nameof(BclExceptionDifferentialKernels.StackLinqSingleKernel)}_TwoElements",
        validLength: 1,
        faultLength: 2,
        static (x, n, d, s) => BclExceptionDifferentialKernels.LaunchStackLinqSingleKernel(
            x, n, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX),
        BclExceptionDifferentialKernels.StackLinqSingleKernel);

    private static readonly DifferentialCase s_minOnEmpty = SliceCase(
        nameof(BclExceptionDifferentialKernels.StackLinqMinKernel),
        validLength: SliceWidth,
        faultLength: 0,
        static (x, n, d, s) => BclExceptionDifferentialKernels.LaunchStackLinqMinKernel(
            x, n, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX),
        BclExceptionDifferentialKernels.StackLinqMinKernel);

    private static readonly DifferentialCase s_maxOnEmpty = SliceCase(
        nameof(BclExceptionDifferentialKernels.StackLinqMaxKernel),
        validLength: SliceWidth,
        faultLength: 0,
        static (x, n, d, s) => BclExceptionDifferentialKernels.LaunchStackLinqMaxKernel(
            x, n, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX),
        BclExceptionDifferentialKernels.StackLinqMaxKernel);

    private static readonly DifferentialCase s_averageOnEmpty = SliceCase(
        nameof(BclExceptionDifferentialKernels.StackLinqAverageKernel),
        validLength: SliceWidth,
        faultLength: 0,
        static (x, n, d, s) => BclExceptionDifferentialKernels.LaunchStackLinqAverageKernel(
            x, n, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX),
        BclExceptionDifferentialKernels.StackLinqAverageKernel);

    private static readonly DifferentialCase s_aggregateOnEmpty = SliceCase(
        nameof(BclExceptionDifferentialKernels.StackLinqAggregateKernel),
        validLength: SliceWidth,
        faultLength: 0,
        static (x, n, d, s) => BclExceptionDifferentialKernels.LaunchStackLinqAggregateKernel(
            x, n, d, s.Rows, gridDimX: s.GridX, blockDimX: s.BlockX),
        BclExceptionDifferentialKernels.StackLinqAggregateKernel);

    private static DifferentialCase ElementAtCase(string suffix, int validIdx, int faultIdx) =>
        new($"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.StackLinqElementAtKernel)}_{suffix}")
        {
            Shape = LaunchShapes.Linear(DefaultRows, 32),
            Seeds = [0, 1],
            OracleExceptions = OracleExceptionMode.MatchOracle,
            Inputs = (rng, shape) =>
            {
                int rows = shape.Rows;
                var backing = new int[rows * SliceWidth];
                var indices = new int[rows];
                for (int k = 0; k < rows; k++)
                {
                    indices[k] = validIdx;
                    for (int j = 0; j < SliceWidth; j++)
                    {
                        backing[(k * SliceWidth) + j] = (k * 10) + j + 1;
                    }
                }

                if (rng.Seed == 1)
                {
                    indices[3] = faultIdx;
                }

                return new DiffBuffers()
                    .In("backing", backing, readOnly: true)
                    .In("indices", indices, readOnly: true)
                    .Out<int>("dst", rows, BclExceptionDifferentialKernels.StackLinqElementAtColumns, OutputPolicy.Exact);
            },
            Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchStackLinqElementAtKernel(
                b.Get<int>("backing"),
                b.Get<int>("indices"),
                b.Get<int>("dst"),
                s.Rows,
                gridDimX: s.GridX,
                blockDimX: s.BlockX),
            Oracle = static (b, s) => BclExceptionDifferentialKernels.StackLinqElementAtKernel(
                b.Get<int>("backing"),
                b.Get<int>("indices"),
                b.Get<int>("dst"),
                s.Rows),
        };

    private static readonly DifferentialCase s_elementAtNegative = ElementAtCase("NegativeIndex", validIdx: 1, faultIdx: -1);
    private static readonly DifferentialCase s_elementAtPastEnd = ElementAtCase("PastEnd", validIdx: 1, faultIdx: SliceWidth);

    private static readonly DifferentialCase s_elementAtOrDefaultNegativeIndexSideEffect = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.ElementAtOrDefaultNegativeIndexSideEffectKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0],
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = new int[rows * SliceWidth];
            for (int k = 0; k < rows; k++)
            {
                for (int j = 0; j < SliceWidth; j++)
                {
                    backing[(k * SliceWidth) + j] = (k * 10) + j + 1;
                }
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .Out<int>(
                    "dst",
                    rows,
                    [
                        "combined_default_sum",
                        "select_neg_eval_count",
                        "where_neg_eval_count",
                    ],
                    OutputPolicy.Exact);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchElementAtOrDefaultNegativeIndexSideEffectKernel(
            b.Get<int>("backing"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Reference = static (b, s) =>
        {
            // Reference: ElementAtOrDefault(-1) returns default (0) without running any upstream side effects.
            // All dst entries remain zero (default buffer state).
        },
    };

    private static readonly DifferentialCase s_sortLengthMismatch = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.MemoryExtensionsSortLengthMismatchKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var keys = new int[rows * SliceWidth];
            var items = new int[rows * SliceWidth];
            var itemLens = new int[rows];

            for (int k = 0; k < rows; k++)
            {
                itemLens[k] = SliceWidth;
                for (int j = 0; j < SliceWidth; j++)
                {
                    keys[(k * SliceWidth) + j] = (SliceWidth - j) * 10;
                    items[(k * SliceWidth) + j] = (SliceWidth - j) * 100;
                }
            }

            if (rng.Seed == 1)
            {
                itemLens[3] = SliceWidth - 1; // length mismatch throws ArgumentException
            }

            return new DiffBuffers()
                .In("keys", keys)
                .In("items", items)
                .In("itemLens", itemLens, readOnly: true)
                .Out<int>("dst", rows, BclExceptionDifferentialKernels.SortExceptionColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchMemoryExtensionsSortLengthMismatchKernel(
            b.Get<int>("keys"),
            b.Get<int>("items"),
            b.Get<int>("itemLens"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.MemoryExtensionsSortLengthMismatchKernel(
            b.Get<int>("keys"),
            b.Get<int>("items"),
            b.Get<int>("itemLens"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_sortComparerTrap = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.MemoryExtensionsSortComparerTrapKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var keys = new int[rows * SliceWidth];
            var trapTriggers = new int[rows];

            for (int k = 0; k < rows; k++)
            {
                trapTriggers[k] = 0;
                for (int j = 0; j < SliceWidth; j++)
                {
                    keys[(k * SliceWidth) + j] = (SliceWidth - j) * 10;
                }
            }

            if (rng.Seed == 1)
            {
                trapTriggers[3] = 1; // comparer trap throws InvalidOperationException
            }

            return new DiffBuffers()
                .In("keys", keys)
                .In("trapTriggers", trapTriggers, readOnly: true)
                .Out<int>("dst", rows, BclExceptionDifferentialKernels.SortExceptionColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchMemoryExtensionsSortComparerTrapKernel(
            b.Get<int>("keys"),
            b.Get<int>("trapTriggers"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.MemoryExtensionsSortComparerTrapKernel(
            b.Get<int>("keys"),
            b.Get<int>("trapTriggers"),
            b.Get<int>("dst"),
            s.Rows),
    };

    private static readonly DifferentialCase s_trapNonContinuationGuard = new(
        $"{nameof(BclExceptionDifferentialKernels)}.{nameof(BclExceptionDifferentialKernels.TrapNonContinuationGuardKernel)}")
    {
        Shape = LaunchShapes.Linear(DefaultRows, 32),
        Seeds = [0, 1],
        OracleExceptions = OracleExceptionMode.MatchOracle,
        SkippedPaths = new Dictionary<DiffPath, string>
        {
            [DiffPath.Native] = "#718 (x86 idiv terminates process)",
        },
        Inputs = static (rng, shape) =>
        {
            int rows = shape.Rows;
            var backing = new int[rows * SliceWidth];
            var thresholds = new int[rows];

            for (int k = 0; k < rows; k++)
            {
                thresholds[k] = 0;
                for (int j = 0; j < SliceWidth; j++)
                {
                    backing[(k * SliceWidth) + j] = j + 2;
                }
            }

            if (rng.Seed == 1)
            {
                thresholds[7] = 1000000; // empty Where sequence causes First() to throw InvalidOperationException
            }

            return new DiffBuffers()
                .In("backing", backing, readOnly: true)
                .In("thresholds", thresholds, readOnly: true)
                .Out<int>("dst", rows, ["quot_after_first"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => BclExceptionDifferentialKernels.LaunchTrapNonContinuationGuardKernel(
            b.Get<int>("backing"),
            b.Get<int>("thresholds"),
            b.Get<int>("dst"),
            s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        Oracle = static (b, s) => BclExceptionDifferentialKernels.TrapNonContinuationGuardKernel(
            b.Get<int>("backing"),
            b.Get<int>("thresholds"),
            b.Get<int>("dst"),
            s.Rows),
    };

    [SkippableFact]
    public void Test_NullableInt32ValueAccess_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableInt32ValueAccess);

    [SkippableFact]
    public void Test_NullableStructValueAccess_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableStructValueAccess);

    [SkippableFact]
    public void Test_MathClampInt32_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathClampInt32);

    [SkippableFact]
    public void Test_MathClampSingle_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathClampSingle);

    [SkippableFact]
    public void Test_MathClampDouble_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathClampDouble);

    [SkippableFact]
    public void Test_StackLinqFirstOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_firstOnEmpty);

    [SkippableFact]
    public void Test_StackLinqLastOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_lastOnEmpty);

    [SkippableFact]
    public void Test_StackLinqSingleOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singleOnEmpty);

    [SkippableFact]
    public void Test_StackLinqSingleOnTwoElements_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singleOnTwoElements);

    [SkippableFact]
    public void Test_StackLinqMinOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_minOnEmpty);

    [SkippableFact]
    public void Test_StackLinqMaxOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_maxOnEmpty);

    [SkippableFact]
    public void Test_StackLinqAverageOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_averageOnEmpty);

    [SkippableFact]
    public void Test_StackLinqAggregateOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_aggregateOnEmpty);

    [SkippableFact]
    public void Test_StackLinqElementAtNegative_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_elementAtNegative);

    [SkippableFact]
    public void Test_StackLinqElementAtPastEnd_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_elementAtPastEnd);

    [SkippableFact]
    public void Test_ElementAtOrDefaultNegativeIndexSideEffect_MatchesReference() =>
        DifferentialRunner.Run(s_elementAtOrDefaultNegativeIndexSideEffect);

    [SkippableFact]
    public void Test_MemoryExtensionsSortLengthMismatch_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_sortLengthMismatch);

    [SkippableFact]
    public void Test_MemoryExtensionsSortComparerTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_sortComparerTrap);

    [SkippableFact]
    public void Test_TrapNonContinuationGuard_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_trapNonContinuationGuard);
}
