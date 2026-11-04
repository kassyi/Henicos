#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Differential)]
[Scenario(TestScenario.Boundary)]
public class StackLinqDifferentialTests
{
    private const int SliceLen = StackLinqDifferentialKernels.SliceLength;

    // Deliberately crafted 4-element int32 rows for seed 0:
    // Row 0: normal positive values
    // Row 1: four int.MaxValue -> sum = 4L * int.MaxValue (overflows int32 twice!)
    // Row 2: four int.MinValue -> sum = 4L * int.MinValue (overflows int32!)
    // Row 3: alternating int.MaxValue and int.MinValue
    // Row 4: all negative
    // Row 5: zeros and ones
    private static readonly int[][] s_int32Seed0Rows =
    [
        [1, 2, 3, 4],
        [int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue],
        [int.MinValue, int.MinValue, int.MinValue, int.MinValue],
        [int.MaxValue, int.MinValue, int.MaxValue, -1],
        [-10, -20, -30, -40],
        [0, -1, 1, 0],
        [int.MaxValue, 1, 1, 1],
        [int.MinValue, -1, -1, -1],
    ];

    // Deliberately crafted 4-element float rows for seed 0:
    // Row 0: [16777216f, 1f, 1f, 1f] -> sum in double is 16777219.0 (/4 = 4194304.75f), whereas float accumulator rounds 16777216f + 1f to 16777216f (/4 = 4194304.0f)
    // Row 1: [float.MaxValue, float.MaxValue, 0f, 0f] -> sum in double is 2*MaxValue (/4 = MaxValue/2), whereas float accumulator overflows to +Infinity
    // Row 2: [1f, 1e-7f, 1e-7f, 1e-7f] -> sum in double is 1.000000300000000, sum/4 in double != (double)(float)(sum/4)
    // Row 3: [1f, 1f, 1f, 0.1f] -> sum = 3.1, / 4 = 0.775 (not exact in binary float/double)
    // Row 4: NaN at index 0: [NaN, 1f, 2f, -3f]
    // Row 5: NaN at index 1: [1f, NaN, 2f, -3f]
    // Row 6: NaN at index 3: [1f, 2f, -3f, NaN]
    // Row 7: [-0.0f, +0.0f, -0.0f, +0.0f]
    // Row 8: [+0.0f, -0.0f, +0.0f, -0.0f]
    // Row 9: [-0.0f, -1.0f, -2.0f, +0.0f] (Max is -0.0f vs +0.0f)
    // Row 10: [+0.0f, 1.0f, 2.0f, -0.0f] (Min is +0.0f vs -0.0f)
    // Row 11: [1f, 0f, 0f, 0f] -> exact in float (0.25f)
    private static readonly float[][] s_singleSeed0Rows =
    [
        [16777216f, 1f, 1f, 1f],
        [float.MaxValue, float.MaxValue, 0f, 0f],
        [1f, 1e-7f, 1e-7f, 1e-7f],
        [1f, 1f, 1f, 0.1f],
        [float.NaN, 1f, 2f, -3f],
        [1f, float.NaN, 2f, -3f],
        [1f, 2f, -3f, float.NaN],
        [-0.0f, +0.0f, -0.0f, +0.0f],
        [+0.0f, -0.0f, +0.0f, -0.0f],
        [-0.0f, -1.0f, -2.0f, +0.0f],
        [+0.0f, 1.0f, 2.0f, -0.0f],
        [1f, 0f, 0f, 0f],
    ];

    private static readonly double[][] s_doubleSeed0Rows =
    [
        [1.0, 2.0, 3.0, 4.0],
        [double.NaN, 1.0, 2.0, -3.0],
        [1.0, double.NaN, 2.0, -3.0],
        [1.0, 2.0, -3.0, double.NaN],
        [-0.0, +0.0, -0.0, +0.0],
        [+0.0, -0.0, +0.0, -0.0],
        [-0.0, -1.0, -2.0, +0.0],
        [+0.0, 1.0, 2.0, -0.0],
    ];

    private static readonly DifferentialCase s_int32Matrix = new($"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.Int32StackLinqMatrixKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, StackLinqDifferentialKernels.Int32StackLinqColumns, OutputPolicy.Exact),
        Launch = static (b, s) => StackLinqDifferentialKernels.LaunchInt32StackLinqMatrixKernel(
            b.Get<int>("xs"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => StackLinqDifferentialKernels.Int32StackLinqMatrixKernel(b.Get<int>("xs"), b.Get<long>("r")),
    };

    private static readonly DifferentialCase s_int32Average = new($"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.Int32AverageKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
            .Out<double>("r", rows, StackLinqDifferentialKernels.Int32AverageColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => StackLinqDifferentialKernels.LaunchInt32AverageKernel(
            b.Get<int>("xs"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => StackLinqDifferentialKernels.Int32AverageKernel(b.Get<int>("xs"), b.Get<double>("r")),
    };

    private static readonly DifferentialCase s_singleFusedAverage = new($"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.SingleFusedAverageKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<double>("r", rows, StackLinqDifferentialKernels.SingleFusedAverageColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => StackLinqDifferentialKernels.LaunchSingleFusedAverageKernel(
            b.Get<float>("xs"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => StackLinqDifferentialKernels.SingleFusedAverageKernel(b.Get<float>("xs"), b.Get<double>("r")),
    };

    private static readonly DifferentialCase s_singleUnfusedAverage = new($"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.SingleUnfusedAverageKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<double>("r", rows, StackLinqDifferentialKernels.SingleUnfusedAverageColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => StackLinqDifferentialKernels.LaunchSingleUnfusedAverageKernel(
            b.Get<float>("xs"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => StackLinqDifferentialKernels.SingleUnfusedAverageKernel(b.Get<float>("xs"), b.Get<double>("r")),
    };

    private static readonly DifferentialCase s_floatingExtremaAndZeroContains = new($"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.FloatingExtremaAndZeroContainsKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("fs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .In("ds", BuildDoubleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<double>("r", rows, StackLinqDifferentialKernels.FloatingExtremaAndZeroContainsColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => StackLinqDifferentialKernels.LaunchFloatingExtremaAndZeroContainsKernel(
            b.Get<float>("fs"),
            b.Get<double>("ds"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => StackLinqDifferentialKernels.FloatingExtremaAndZeroContainsKernel(
            b.Get<float>("fs"),
            b.Get<double>("ds"),
            b.Get<double>("r")
        ),
    };

    private static readonly DifferentialCase s_containsNaN = new($"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.ContainsNaNKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("fs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .In("ds", BuildDoubleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<long>("r", rows, StackLinqDifferentialKernels.ContainsNaNColumns, OutputPolicy.Exact),
        Launch = static (b, s) => StackLinqDifferentialKernels.LaunchContainsNaNKernel(
            b.Get<float>("fs"),
            b.Get<double>("ds"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => StackLinqDifferentialKernels.ContainsNaNKernel(
            b.Get<float>("fs"),
            b.Get<double>("ds"),
            b.Get<long>("r")
        ),
    };

    private static readonly DifferentialCase s_containsEvaluationOrder = new($"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.ContainsEvaluationOrderKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, StackLinqDifferentialKernels.ContainsEvaluationOrderColumns, OutputPolicy.Exact),
        Launch = static (b, s) => StackLinqDifferentialKernels.LaunchContainsEvaluationOrderKernel(
            b.Get<int>("xs"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => StackLinqDifferentialKernels.ContainsEvaluationOrderKernel(b.Get<int>("xs"), b.Get<long>("r")),
    };

    private static readonly DifferentialCase s_unfusedSelectEvaluation = new($"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.UnfusedSelectEvaluationKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, StackLinqDifferentialKernels.UnfusedSelectEvaluationColumns, OutputPolicy.Exact),
        Launch = static (b, s) => StackLinqDifferentialKernels.LaunchUnfusedSelectEvaluationKernel(
            b.Get<int>("xs"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => StackLinqDifferentialKernels.UnfusedSelectEvaluationKernel(b.Get<int>("xs"), b.Get<long>("r")),
    };

    private static readonly DifferentialCase s_narrowAndInt64 = new($"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.NarrowAndInt64StackLinqKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) =>
        {
            var (bs, ss, ls) = BuildNarrowAndInt64Slices(rng, rows);
            return new DiffBuffers()
                .In("bs", bs, OutputPolicy.Exact)
                .In("ss", ss, OutputPolicy.Exact)
                .In("ls", ls, OutputPolicy.Exact)
                .Out<long>("r", rows, StackLinqDifferentialKernels.NarrowAndInt64StackLinqColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => StackLinqDifferentialKernels.LaunchNarrowAndInt64StackLinqKernel(
            b.Get<byte>("bs"),
            b.Get<short>("ss"),
            b.Get<long>("ls"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => StackLinqDifferentialKernels.NarrowAndInt64StackLinqKernel(
            b.Get<byte>("bs"),
            b.Get<short>("ss"),
            b.Get<long>("ls"),
            b.Get<long>("r")
        ),
    };

    private static readonly DifferentialCase s_floatSequentialSum = new($"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.FloatSequentialSumKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<float>("r", rows, StackLinqDifferentialKernels.FloatSequentialSumColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => StackLinqDifferentialKernels.LaunchFloatSequentialSumKernel(
            b.Get<float>("xs"),
            b.Get<float>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Reference = static (b, _) =>
        {
            var xs = b.Get<float>("xs");
            var r = b.Get<float>("r");
            int rows = xs.Length / SliceLen;
            for (int t = 0; t < rows; t++)
            {
                float sum = 0f;
                for (int i = 0; i < SliceLen; i++)
                {
                    sum += xs[t * SliceLen + i];
                }

                int o = t * 3;
                r[o] = sum;
                r[o + 1] = sum;
                r[o + 2] = sum;
            }
        },
    };

    private static readonly DifferentialCase s_doubleSequentialSum = new($"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.DoubleSequentialSumKernel)}")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildDoubleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<double>("r", rows, StackLinqDifferentialKernels.DoubleSequentialSumColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => StackLinqDifferentialKernels.LaunchDoubleSequentialSumKernel(
            b.Get<double>("xs"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Reference = static (b, _) =>
        {
            var xs = b.Get<double>("xs");
            var r = b.Get<double>("r");
            int rows = xs.Length / SliceLen;
            for (int t = 0; t < rows; t++)
            {
                double sum = 0.0;
                double selSum = 0.0;
                for (int i = 0; i < SliceLen; i++)
                {
                    double val = xs[t * SliceLen + i];
                    sum += val;
                    selSum += val * 2.0;
                }

                int o = t * 4;
                r[o + 0] = sum;
                r[o + 1] = sum;
                r[o + 2] = sum;
                r[o + 3] = selSum;
            }
        },
    };

    private static DifferentialCase CreateSingleElementCase(
        string kernelName,
        string column,
        Action<int[], long[], uint, uint> launch,
        Action<int[], long[]> oracle) =>
        new($"{nameof(StackLinqDifferentialKernels)}.{kernelName}")
        {
            Shape = LaunchShape.ForRows(64, blockX: 32),
            Inputs = (rng, rows) => new DiffBuffers()
                .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
                .Out<long>("r", rows, [column], OutputPolicy.Exact),
            Launch = (b, s) => launch(b.Get<int>("xs"), b.Get<long>("r"), s.GridX, s.BlockX),
            Oracle = (b, _) => oracle(b.Get<int>("xs"), b.Get<long>("r")),
        };

    private static readonly DifferentialCase s_fusedFirst = CreateSingleElementCase(
        nameof(StackLinqDifferentialKernels.StackLinqFusedFirstKernel),
        "FusedFirst",
        static (xs, r, gx, bx) => StackLinqDifferentialKernels.LaunchStackLinqFusedFirstKernel(xs, r, gridDimX: gx, blockDimX: bx),
        StackLinqDifferentialKernels.StackLinqFusedFirstKernel);

    private static readonly DifferentialCase s_fusedFirstPred = CreateSingleElementCase(
        nameof(StackLinqDifferentialKernels.StackLinqFusedFirstPredicateKernel),
        "FusedFirstPred",
        static (xs, r, gx, bx) => StackLinqDifferentialKernels.LaunchStackLinqFusedFirstPredicateKernel(xs, r, gridDimX: gx, blockDimX: bx),
        StackLinqDifferentialKernels.StackLinqFusedFirstPredicateKernel);

    private static readonly DifferentialCase s_fusedLast = CreateSingleElementCase(
        nameof(StackLinqDifferentialKernels.StackLinqFusedLastKernel),
        "FusedLast",
        static (xs, r, gx, bx) => StackLinqDifferentialKernels.LaunchStackLinqFusedLastKernel(xs, r, gridDimX: gx, blockDimX: bx),
        StackLinqDifferentialKernels.StackLinqFusedLastKernel);

    private static readonly DifferentialCase s_fusedLastPred = CreateSingleElementCase(
        nameof(StackLinqDifferentialKernels.StackLinqFusedLastPredicateKernel),
        "FusedLastPred",
        static (xs, r, gx, bx) => StackLinqDifferentialKernels.LaunchStackLinqFusedLastPredicateKernel(xs, r, gridDimX: gx, blockDimX: bx),
        StackLinqDifferentialKernels.StackLinqFusedLastPredicateKernel);

    private static readonly DifferentialCase s_singleOneElem = CreateSingleElementCase(
        nameof(StackLinqDifferentialKernels.StackLinqSingleOneElementKernel),
        "SingleOneElem",
        static (xs, r, gx, bx) => StackLinqDifferentialKernels.LaunchStackLinqSingleOneElementKernel(xs, r, gridDimX: gx, blockDimX: bx),
        StackLinqDifferentialKernels.StackLinqSingleOneElementKernel);

    private static readonly DifferentialCase s_singlePredUnique = CreateSingleElementCase(
        nameof(StackLinqDifferentialKernels.StackLinqSinglePredicateUniqueKernel),
        "SinglePredUnique",
        static (xs, r, gx, bx) => StackLinqDifferentialKernels.LaunchStackLinqSinglePredicateUniqueKernel(xs, r, gridDimX: gx, blockDimX: bx),
        StackLinqDifferentialKernels.StackLinqSinglePredicateUniqueKernel);

    private static readonly DifferentialCase s_singleOrDefaultEmpty = CreateSingleElementCase(
        nameof(StackLinqDifferentialKernels.StackLinqSingleOrDefaultEmptyKernel),
        "SingleOrDefaultEmpty",
        static (xs, r, gx, bx) => StackLinqDifferentialKernels.LaunchStackLinqSingleOrDefaultEmptyKernel(xs, r, gridDimX: gx, blockDimX: bx),
        StackLinqDifferentialKernels.StackLinqSingleOrDefaultEmptyKernel);

    private static readonly DifferentialCase s_singleOrDefaultOneElem = CreateSingleElementCase(
        nameof(StackLinqDifferentialKernels.StackLinqSingleOrDefaultOneElementKernel),
        "SingleOrDefaultOneElem",
        static (xs, r, gx, bx) => StackLinqDifferentialKernels.LaunchStackLinqSingleOrDefaultOneElementKernel(xs, r, gridDimX: gx, blockDimX: bx),
        StackLinqDifferentialKernels.StackLinqSingleOrDefaultOneElementKernel);

    private static readonly DifferentialCase s_singleOrDefaultPredNone = CreateSingleElementCase(
        nameof(StackLinqDifferentialKernels.StackLinqSingleOrDefaultPredicateNoneKernel),
        "SingleOrDefaultPredNone",
        static (xs, r, gx, bx) => StackLinqDifferentialKernels.LaunchStackLinqSingleOrDefaultPredicateNoneKernel(xs, r, gridDimX: gx, blockDimX: bx),
        StackLinqDifferentialKernels.StackLinqSingleOrDefaultPredicateNoneKernel);

    private static readonly DifferentialCase s_elementAt0 = CreateSingleElementCase(
        nameof(StackLinqDifferentialKernels.StackLinqElementAtZeroKernel),
        "ElementAt0",
        static (xs, r, gx, bx) => StackLinqDifferentialKernels.LaunchStackLinqElementAtZeroKernel(xs, r, gridDimX: gx, blockDimX: bx),
        StackLinqDifferentialKernels.StackLinqElementAtZeroKernel);

    private static readonly DifferentialCase s_elementAt2 = CreateSingleElementCase(
        nameof(StackLinqDifferentialKernels.StackLinqElementAtTwoKernel),
        "ElementAt2",
        static (xs, r, gx, bx) => StackLinqDifferentialKernels.LaunchStackLinqElementAtTwoKernel(xs, r, gridDimX: gx, blockDimX: bx),
        StackLinqDifferentialKernels.StackLinqElementAtTwoKernel);

    private static readonly DifferentialCase s_takeWhileSideEffects = CreateSingleElementCase(
        nameof(StackLinqDifferentialKernels.StackLinqTakeWhileSideEffectCountKernel),
        "TakeWhileSideEffectCount",
        static (xs, r, gx, bx) => StackLinqDifferentialKernels.LaunchStackLinqTakeWhileSideEffectCountKernel(xs, r, gridDimX: gx, blockDimX: bx),
        StackLinqDifferentialKernels.StackLinqTakeWhileSideEffectCountKernel);

    private static readonly DifferentialCase s_skipWhileSideEffects = CreateSingleElementCase(
        nameof(StackLinqDifferentialKernels.StackLinqSkipWhileSideEffectCountKernel),
        "SkipWhileSideEffectCount",
        static (xs, r, gx, bx) => StackLinqDifferentialKernels.LaunchStackLinqSkipWhileSideEffectCountKernel(xs, r, gridDimX: gx, blockDimX: bx),
        StackLinqDifferentialKernels.StackLinqSkipWhileSideEffectCountKernel);

    private static readonly DifferentialCase s_parallelSum = new(
        $"{nameof(StackLinqDifferentialKernels)}.{nameof(StackLinqDifferentialKernels.ParallelSumStackLinqKernel)}")
    {
        Shape = LaunchShapes.Grid1D(gridX: 1, blockX: 64, rows: 64),
        Inputs = static (rng, shape) =>
        {
            var xs = DiffValues.Int32(rng, shape.Rows);
            for (int i = 0; i < xs.Length; i++)
            {
                xs[i] &= 0xFFFF;
            }

            return new DiffBuffers()
                .In("xs", xs, OutputPolicy.Exact)
                .Out<int>("r", shape.Rows, ["BlockParallelSum", "WhereSelectParallelSum"], OutputPolicy.Exact);
        },
        Launch = static (b, s) => StackLinqDifferentialKernels.LaunchParallelSumStackLinqKernel(
            b.Get<int>("xs"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Reference = static (b, s) =>
        {
            var xs = b.Get<int>("xs");
            var r = b.Get<int>("r");
            int blockSum = 0;
            int filteredSum = 0;
            for (int i = 0; i < xs.Length; i++)
            {
                blockSum += xs[i];
                if ((xs[i] & 1) == 0)
                {
                    filteredSum += xs[i] * 2;
                }
            }

            for (int t = 0; t < s.Rows; t++)
            {
                r[t * 2] = blockSum;
                r[t * 2 + 1] = filteredSum;
            }
        },
    };

    [SkippableFact]
    public void Test_Int32StackLinqMatrix_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_int32Matrix);

    [SkippableFact]
    public void Test_Int32Average_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_int32Average);

    [SkippableFact]
    public void Test_SingleFusedAverage_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_singleFusedAverage);

    [SkippableFact]
    public void Test_SingleUnfusedAverage_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_singleUnfusedAverage);

    [SkippableFact]
    public void Test_FloatingExtremaAndZeroContains_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_floatingExtremaAndZeroContains);

    [SkippableFact]
    public void Test_ContainsNaN_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_containsNaN);

    [SkippableFact]
    public void Test_ContainsEvaluationOrder_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_containsEvaluationOrder);

    [SkippableFact]
    public void Test_UnfusedSelectEvaluation_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_unfusedSelectEvaluation);

    [SkippableFact]
    public void Test_NarrowAndInt64StackLinq_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_narrowAndInt64);

    [SkippableFact]
    public void Test_FloatSequentialSum_MatchesTheReferenceOnEveryPath() => DifferentialRunner.Run(s_floatSequentialSum);

    [SkippableFact]
    public void Test_DoubleSequentialSum_MatchesTheReferenceOnEveryPath() => DifferentialRunner.Run(s_doubleSequentialSum);

    [SkippableFact]
    public void Test_StackLinqFusedFirst_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_fusedFirst);

    [SkippableFact]
    public void Test_StackLinqFusedFirstPredicate_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_fusedFirstPred);

    [SkippableFact]
    public void Test_StackLinqFusedLast_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_fusedLast);

    [SkippableFact]
    public void Test_StackLinqFusedLastPredicate_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_fusedLastPred);

    [SkippableFact]
    public void Test_StackLinqSingleOneElement_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_singleOneElem);

    [SkippableFact]
    public void Test_StackLinqSinglePredicateUnique_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_singlePredUnique);

    [SkippableFact]
    public void Test_StackLinqSingleOrDefaultEmpty_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_singleOrDefaultEmpty);

    [SkippableFact]
    public void Test_StackLinqSingleOrDefaultOneElement_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_singleOrDefaultOneElem);

    [SkippableFact]
    public void Test_StackLinqSingleOrDefaultPredicateNone_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_singleOrDefaultPredNone);

    [SkippableFact]
    public void Test_StackLinqElementAtZero_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_elementAt0);

    [SkippableFact]
    public void Test_StackLinqElementAtTwo_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_elementAt2);

    [SkippableFact]
    public void Test_StackLinqTakeWhileSideEffects_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_takeWhileSideEffects);

    [SkippableFact]
    public void Test_StackLinqSkipWhileSideEffects_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_skipWhileSideEffects);

    [SkippableFact]
    public void Test_ParallelSumStackLinq_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_parallelSum);



    private static int[] BuildInt32Slices(DiffRandom rng, int rows)
    {
        var values = DiffValues.Int32(rng, rows * SliceLen);
        if (rng.IsBoundarySeed)
        {
            for (int r = 0; r < s_int32Seed0Rows.Length && r < rows; r++)
            {
                Array.Copy(s_int32Seed0Rows[r], 0, values, r * SliceLen, SliceLen);
            }
        }

        return values;
    }

    private static float[] BuildSingleSlices(DiffRandom rng, int rows)
    {
        var values = DiffValues.Single(rng, rows * SliceLen);
        if (rng.IsBoundarySeed)
        {
            for (int r = 0; r < s_singleSeed0Rows.Length && r < rows; r++)
            {
                Array.Copy(s_singleSeed0Rows[r], 0, values, r * SliceLen, SliceLen);
            }
        }

        return values;
    }

    private static double[] BuildDoubleSlices(DiffRandom rng, int rows)
    {
        var singles = DiffValues.Single(rng, rows * SliceLen);
        var values = new double[rows * SliceLen];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = singles[i];
        }

        if (rng.IsBoundarySeed)
        {
            for (int r = 0; r < s_doubleSeed0Rows.Length && r < rows; r++)
            {
                Array.Copy(s_doubleSeed0Rows[r], 0, values, r * SliceLen, SliceLen);
            }
        }

        return values;
    }

    private static (byte[] Bs, short[] Ss, long[] Ls) BuildNarrowAndInt64Slices(DiffRandom rng, int rows)
    {
        var ints = BuildInt32Slices(rng, rows);
        var bs = new byte[ints.Length];
        var ss = new short[ints.Length];
        var ls = new long[ints.Length];
        for (int i = 0; i < ints.Length; i++)
        {
            bs[i] = unchecked((byte)ints[i]);
            ss[i] = unchecked((short)ints[i]);
            ls[i] = ints[i] * 0x100000001L;
        }

        if (rng.IsBoundarySeed)
        {
            // Row 0: large byte/short values that overflow byte/short when summed
            bs[0] = 200; bs[1] = 200; bs[2] = 200; bs[3] = 200;
            ss[0] = 30000; ss[1] = 30000; ss[2] = 30000; ss[3] = 30000;
            ls[0] = long.MaxValue; ls[1] = 1L; ls[2] = long.MinValue; ls[3] = -1L;
        }

        return (bs, ss, ls);
    }
}
