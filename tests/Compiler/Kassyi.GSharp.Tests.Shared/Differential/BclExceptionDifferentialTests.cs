#nullable enable
using System;
using System.Runtime.CompilerServices;
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

    internal static readonly DifferentialCase s_nullableInt32ValueAccess = DiffCases.Create(
        BclExceptionDifferentialKernels.LaunchNullableInt32ValueAccessKernel,
        inputs: static (rng, shape) =>
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
        shape: LaunchShapes.Linear(DefaultRows, 32),
        firstInputName: "hasInt",
        secondInputName: "intVals",
        seeds: [0, 1],
        oracleExceptions: OracleExceptionMode.MatchOracle);

    internal static readonly DifferentialCase s_nullableStructValueAccess = new(
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
            inputs: new NullableStructPairInputs
            {
                HasPairFlags = b.Get<int>("hasPair"),
                PairXValues = b.Get<int>("pairX"),
                PairYValues = b.Get<int>("pairY"),
            },
            results: b.Get<int>("dst"),
            rows: s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_mathClampInt32 = new(
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
            inputs: new Int32ClampInputs
            {
                Values = b.Get<int>("vals"),
                Minimums = b.Get<int>("mins"),
                Maximums = b.Get<int>("maxs"),
            },
            results: b.Get<int>("dst"),
            rows: s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_mathClampSingle = new(
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
            inputs: new SingleClampInputs
            {
                Values = b.Get<float>("vals"),
                Minimums = b.Get<float>("mins"),
                Maximums = b.Get<float>("maxs"),
            },
            results: b.Get<float>("dst"),
            rows: s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_mathClampDouble = new(
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
            inputs: new DoubleClampInputs
            {
                Values = b.Get<double>("vals"),
                Minimums = b.Get<double>("mins"),
                Maximums = b.Get<double>("maxs"),
            },
            results: b.Get<double>("dst"),
            rows: s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

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
        Int32PairRowsLaunch launch,
        int validLength,
        int faultLength,
        string? suffix = null,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: (rng, shape) => SliceInputs(shape.Rows, validLength, rng.Seed == 1 ? faultLength : validLength),
            shape: LaunchShapes.Linear(DefaultRows, 32),
            firstInputName: "backing",
            secondInputName: "lengths",
            suffix: suffix,
            seeds: [0, 1],
            oracleExceptions: OracleExceptionMode.MatchOracle,
            launchExpression: launchExpression);

    internal static readonly DifferentialCase s_firstOnEmpty = SliceCase(
        BclExceptionDifferentialKernels.LaunchStackLinqFirstKernel,
        validLength: SliceWidth,
        faultLength: 0);

    internal static readonly DifferentialCase s_lastOnEmpty = SliceCase(
        BclExceptionDifferentialKernels.LaunchStackLinqLastKernel,
        validLength: SliceWidth,
        faultLength: 0);

    internal static readonly DifferentialCase s_singleOnEmpty = SliceCase(
        BclExceptionDifferentialKernels.LaunchStackLinqSingleKernel,
        validLength: 1,
        faultLength: 0);

    internal static readonly DifferentialCase s_singleOnTwoElements = SliceCase(
        BclExceptionDifferentialKernels.LaunchStackLinqSingleKernel,
        validLength: 1,
        faultLength: 2,
        suffix: "_TwoElements");

    internal static readonly DifferentialCase s_minOnEmpty = SliceCase(
        BclExceptionDifferentialKernels.LaunchStackLinqMinKernel,
        validLength: SliceWidth,
        faultLength: 0);

    internal static readonly DifferentialCase s_maxOnEmpty = SliceCase(
        BclExceptionDifferentialKernels.LaunchStackLinqMaxKernel,
        validLength: SliceWidth,
        faultLength: 0);

    internal static readonly DifferentialCase s_averageOnEmpty = SliceCase(
        BclExceptionDifferentialKernels.LaunchStackLinqAverageKernel,
        validLength: SliceWidth,
        faultLength: 0);

    internal static readonly DifferentialCase s_aggregateOnEmpty = SliceCase(
        BclExceptionDifferentialKernels.LaunchStackLinqAggregateKernel,
        validLength: SliceWidth,
        faultLength: 0);

    private static DifferentialCase ElementAtCase(string suffix, int validIdx, int faultIdx) =>
        DiffCases.Create(
            BclExceptionDifferentialKernels.LaunchStackLinqElementAtKernel,
            inputs: (rng, shape) =>
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
            shape: LaunchShapes.Linear(DefaultRows, 32),
            firstInputName: "backing",
            secondInputName: "indices",
            suffix: $"_{suffix}",
            seeds: [0, 1],
            oracleExceptions: OracleExceptionMode.MatchOracle);

    internal static readonly DifferentialCase s_elementAtNegative = ElementAtCase("NegativeIndex", validIdx: 1, faultIdx: -1);
    internal static readonly DifferentialCase s_elementAtPastEnd = ElementAtCase("PastEnd", validIdx: 1, faultIdx: SliceWidth);

    internal static readonly DifferentialCase s_elementAtOrDefaultNegativeIndexSideEffect = DiffCases.Create(
        BclExceptionDifferentialKernels.LaunchElementAtOrDefaultNegativeIndexSideEffectKernel,
        inputs: static (rng, shape) =>
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
        shape: LaunchShapes.Linear(DefaultRows, 32),
        inputName: "backing",
        seeds: [0],
        reference: static (b, s) =>
        {
            // Reference: ElementAtOrDefault(-1) returns default (0) without running any upstream side effects.
            // All dst entries remain zero (default buffer state).
        });

    internal static readonly DifferentialCase s_sortLengthMismatch = new(
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
            buffers: new SortLengthMismatchBuffers
            {
                KeysBacking = b.Get<int>("keys"),
                ItemsBacking = b.Get<int>("items"),
                ItemLengths = b.Get<int>("itemLens"),
            },
            results: b.Get<int>("dst"),
            rows: s.Rows,
            gridDimX: s.GridX,
            blockDimX: s.BlockX),
        OracleIsLaunch = true,
    };

    internal static readonly DifferentialCase s_sortComparerTrap = DiffCases.Create(
        BclExceptionDifferentialKernels.LaunchMemoryExtensionsSortComparerTrapKernel,
        inputs: static (rng, shape) =>
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
        shape: LaunchShapes.Linear(DefaultRows, 32),
        firstInputName: "keys",
        secondInputName: "trapTriggers",
        seeds: [0, 1],
        oracleExceptions: OracleExceptionMode.MatchOracle);

    internal static readonly DifferentialCase s_trapNonContinuationGuard = DiffCases.Create(
        BclExceptionDifferentialKernels.LaunchTrapNonContinuationGuardKernel,
        inputs: static (rng, shape) =>
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
        shape: LaunchShapes.Linear(DefaultRows, 32),
        firstInputName: "backing",
        secondInputName: "thresholds",
        seeds: [0, 1],
        oracleExceptions: OracleExceptionMode.MatchOracle);

    [SkippableFact]
    public void ValueAccess_NullableInt32_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableInt32ValueAccess);

    [SkippableFact]
    public void ValueAccess_NullableStruct_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_nullableStructValueAccess);

    [SkippableFact]
    public void MathClamp_Int32_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathClampInt32);

    [SkippableFact]
    public void MathClamp_Single_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathClampSingle);

    [SkippableFact]
    public void MathClamp_Double_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathClampDouble);

    [SkippableFact]
    public void StackLinq_FirstOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_firstOnEmpty);

    [SkippableFact]
    public void StackLinq_LastOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_lastOnEmpty);

    [SkippableFact]
    public void StackLinq_SingleOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singleOnEmpty);

    [SkippableFact]
    public void StackLinq_SingleOnTwoElements_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singleOnTwoElements);

    [SkippableFact]
    public void StackLinq_MinOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_minOnEmpty);

    [SkippableFact]
    public void StackLinq_MaxOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_maxOnEmpty);

    [SkippableFact]
    public void StackLinq_AverageOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_averageOnEmpty);

    [SkippableFact]
    public void StackLinq_AggregateOnEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_aggregateOnEmpty);

    [SkippableFact]
    public void StackLinq_ElementAtNegative_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_elementAtNegative);

    [SkippableFact]
    public void StackLinq_ElementAtPastEnd_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_elementAtPastEnd);

    [SkippableFact]
    public void ElementAtOrDefault_NegativeIndexSideEffect_MatchesReference() =>
        DifferentialRunner.Run(s_elementAtOrDefaultNegativeIndexSideEffect);

    [SkippableFact]
    public void MemoryExtensionsSort_LengthMismatch_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_sortLengthMismatch);

    [SkippableFact]
    public void MemoryExtensionsSort_ComparerTrap_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_sortComparerTrap);

    [SkippableFact]
    public void TrapGuard_NonContinuation_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_trapNonContinuationGuard);
}
