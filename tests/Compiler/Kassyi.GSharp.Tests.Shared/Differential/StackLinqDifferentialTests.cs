#nullable enable
using System;
using System.Runtime.CompilerServices;
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
    // Row 1: int.MaxValue with elements that cancel part of it
    // Row 2: int.MinValue with elements that cancel part of it
    // Row 3: int.MaxValue and int.MinValue
    // A StackLinq Sum is exact and throws when the exact sum does not fit, and the oracle must not throw, so no sum of
    // any elements of a row leaves the range of int32 (the Sum overflow itself is tested by ParallelAggregateTests)
    // Row 4: all negative
    // Row 5: zeros and ones
    private static readonly int[][] s_int32Seed0Rows =
    [
        [1, 2, 3, 4],
        [int.MaxValue, 0, 0, -1],
        [int.MinValue, 0, 0, 1],
        [int.MaxValue, int.MinValue, 0, 0],
        [-10, -20, -30, -40],
        [0, -1, 1, 0],
        [int.MaxValue, -1, -1, -1],
        [int.MinValue, 1, 1, 1],
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
    // Row 12: every element NaN (Max is NaN only here)
    // Row 13: two leading NaN (Max skips both, Min stops at the first)
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
        [float.NaN, float.NaN, float.NaN, float.NaN],
        [float.NaN, float.NaN, 1f, -3f],
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
        [double.NaN, double.NaN, double.NaN, double.NaN],
        [double.NaN, double.NaN, 1.0, -3.0],
    ];

    internal static readonly DifferentialCase s_int32Matrix = DiffCases.Create(
        StackLinqDifferentialKernels.LaunchInt32StackLinqMatrixKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, StackLinqDifferentialKernels.Int32StackLinqColumns, OutputPolicy.Exact),
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        inputName: "xs",
        outputName: "r");

    internal static readonly DifferentialCase s_int32Average = DiffCases.Create<int, double>(
        StackLinqDifferentialKernels.LaunchInt32AverageKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
            .Out<double>("r", rows, StackLinqDifferentialKernels.Int32AverageColumns, OutputPolicy.Bitwise),
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        inputName: "xs",
        outputName: "r");

    internal static readonly DifferentialCase s_singleFusedAverage = DiffCases.Create<float, double>(
        StackLinqDifferentialKernels.LaunchSingleFusedAverageKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<double>("r", rows, StackLinqDifferentialKernels.SingleFusedAverageColumns, OutputPolicy.Bitwise),
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        inputName: "xs",
        outputName: "r");

    internal static readonly DifferentialCase s_singleUnfusedAverage = DiffCases.Create<float, double>(
        StackLinqDifferentialKernels.LaunchSingleUnfusedAverageKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<double>("r", rows, StackLinqDifferentialKernels.SingleUnfusedAverageColumns, OutputPolicy.Bitwise),
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        inputName: "xs",
        outputName: "r");

    internal static readonly DifferentialCase s_floatingExtremaAndZeroContains = DiffCases.Create<float, double, double>(
        StackLinqDifferentialKernels.LaunchFloatingExtremaAndZeroContainsKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("fs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .In("ds", BuildDoubleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<double>("r", rows, StackLinqDifferentialKernels.FloatingExtremaAndZeroContainsColumns, OutputPolicy.Bitwise),
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        firstInputName: "fs",
        secondInputName: "ds",
        outputName: "r");

    internal static readonly DifferentialCase s_containsNaN = DiffCases.Create<float, double, long>(
        StackLinqDifferentialKernels.LaunchContainsNaNKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("fs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .In("ds", BuildDoubleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<long>("r", rows, StackLinqDifferentialKernels.ContainsNaNColumns, OutputPolicy.Exact),
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        firstInputName: "fs",
        secondInputName: "ds",
        outputName: "r");

    internal static readonly DifferentialCase s_containsEvaluationOrder = DiffCases.Create(
        StackLinqDifferentialKernels.LaunchContainsEvaluationOrderKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, StackLinqDifferentialKernels.ContainsEvaluationOrderColumns, OutputPolicy.Exact),
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        inputName: "xs",
        outputName: "r");

    internal static readonly DifferentialCase s_unfusedSelectEvaluation = DiffCases.Create(
        StackLinqDifferentialKernels.LaunchUnfusedSelectEvaluationKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, StackLinqDifferentialKernels.UnfusedSelectEvaluationColumns, OutputPolicy.Exact),
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        inputName: "xs",
        outputName: "r");

    internal static readonly DifferentialCase s_narrowAndInt64 = DiffCases.Create<byte, short, long, long>(
        StackLinqDifferentialKernels.LaunchNarrowAndInt64StackLinqKernel,
        inputs: static (rng, rows) =>
        {
            var (bs, ss, ls) = BuildNarrowAndInt64Slices(rng, rows);
            return new DiffBuffers()
                .In("bs", bs, OutputPolicy.Exact)
                .In("ss", ss, OutputPolicy.Exact)
                .In("ls", ls, OutputPolicy.Exact)
                .Out<long>("r", rows, StackLinqDifferentialKernels.NarrowAndInt64StackLinqColumns, OutputPolicy.Exact);
        },
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        firstInputName: "bs",
        secondInputName: "ss",
        thirdInputName: "ls",
        outputName: "r");

    internal static readonly DifferentialCase s_floatSequentialSum = DiffCases.Create(
        StackLinqDifferentialKernels.LaunchFloatSequentialSumKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<float>("r", rows, StackLinqDifferentialKernels.FloatSequentialSumColumns, OutputPolicy.Bitwise),
        shapes: LaunchShapes.OneDimensionalMatrix(64),
        inputName: "xs",
        outputName: "r",
        reference: static (b, _) =>
        {
            var xs = b.Get<float>("xs");
            var r = b.Get<float>("r");
            int rows = xs.Length / SliceLen;
            for (int t = 0; t < rows; t++)
            {
                float sum = System.Linq.Enumerable.Sum(xs.AsSpan(t * SliceLen, SliceLen).ToArray());

                int o = t * 3;
                r[o] = sum;
                r[o + 1] = sum;
                r[o + 2] = sum;
            }
        });

    internal static readonly DifferentialCase s_doubleSequentialSum = DiffCases.Create<double, double>(
        StackLinqDifferentialKernels.LaunchDoubleSequentialSumKernel,
        inputs: static (rng, rows) => new DiffBuffers()
            .In("xs", BuildDoubleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<double>("r", rows, StackLinqDifferentialKernels.DoubleSequentialSumColumns, OutputPolicy.Bitwise),
        shape: LaunchShape.ForRows(64, blockX: 32),
        inputName: "xs",
        outputName: "r",
        reference: static (b, _) =>
        {
            var xs = b.Get<double>("xs");
            var r = b.Get<double>("r");
            int rows = xs.Length / SliceLen;
            for (int t = 0; t < rows; t++)
            {
                var slice = xs.AsSpan(t * SliceLen, SliceLen).ToArray();
                double sum = System.Linq.Enumerable.Sum(slice);
                double selSum = System.Linq.Enumerable.Sum(slice, static x => x * 2.0);

                int o = t * 4;
                r[o + 0] = sum;
                r[o + 1] = sum;
                r[o + 2] = sum;
                r[o + 3] = selSum;
            }
        });

    private static DifferentialCase CreateSingleElementCase(
        Int32ToInt64Launch launch,
        string column,
        [CallerArgumentExpression(nameof(launch))] string launchExpression = "") =>
        DiffCases.Create(
            launch,
            inputs: (rng, rows) => new DiffBuffers()
                .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
                .Out<long>("r", rows, [column], OutputPolicy.Exact),
            shape: LaunchShape.ForRows(64, blockX: 32),
            inputName: "xs",
            outputName: "r",
            launchExpression: launchExpression);

    internal static readonly DifferentialCase s_fusedFirst = CreateSingleElementCase(
        StackLinqDifferentialKernels.LaunchStackLinqFusedFirstKernel,
        "FusedFirst");

    internal static readonly DifferentialCase s_fusedFirstPred = CreateSingleElementCase(
        StackLinqDifferentialKernels.LaunchStackLinqFusedFirstPredicateKernel,
        "FusedFirstPred");

    internal static readonly DifferentialCase s_fusedLast = CreateSingleElementCase(
        StackLinqDifferentialKernels.LaunchStackLinqFusedLastKernel,
        "FusedLast");

    internal static readonly DifferentialCase s_fusedLastPred = CreateSingleElementCase(
        StackLinqDifferentialKernels.LaunchStackLinqFusedLastPredicateKernel,
        "FusedLastPred");

    internal static readonly DifferentialCase s_singleOneElem = CreateSingleElementCase(
        StackLinqDifferentialKernels.LaunchStackLinqSingleOneElementKernel,
        "SingleOneElem");

    internal static readonly DifferentialCase s_singlePredUnique = CreateSingleElementCase(
        StackLinqDifferentialKernels.LaunchStackLinqSinglePredicateUniqueKernel,
        "SinglePredUnique");

    internal static readonly DifferentialCase s_singleOrDefaultEmpty = CreateSingleElementCase(
        StackLinqDifferentialKernels.LaunchStackLinqSingleOrDefaultEmptyKernel,
        "SingleOrDefaultEmpty");

    internal static readonly DifferentialCase s_singleOrDefaultOneElem = CreateSingleElementCase(
        StackLinqDifferentialKernels.LaunchStackLinqSingleOrDefaultOneElementKernel,
        "SingleOrDefaultOneElem");

    internal static readonly DifferentialCase s_singleOrDefaultPredNone = CreateSingleElementCase(
        StackLinqDifferentialKernels.LaunchStackLinqSingleOrDefaultPredicateNoneKernel,
        "SingleOrDefaultPredNone");

    internal static readonly DifferentialCase s_elementAt0 = CreateSingleElementCase(
        StackLinqDifferentialKernels.LaunchStackLinqElementAtZeroKernel,
        "ElementAt0");

    internal static readonly DifferentialCase s_elementAt2 = CreateSingleElementCase(
        StackLinqDifferentialKernels.LaunchStackLinqElementAtTwoKernel,
        "ElementAt2");

    internal static readonly DifferentialCase s_takeWhileSideEffects = CreateSingleElementCase(
        StackLinqDifferentialKernels.LaunchStackLinqTakeWhileSideEffectCountKernel,
        "TakeWhileSideEffectCount");

    internal static readonly DifferentialCase s_skipWhileSideEffects = CreateSingleElementCase(
        StackLinqDifferentialKernels.LaunchStackLinqSkipWhileSideEffectCountKernel,
        "SkipWhileSideEffectCount");

    internal static readonly DifferentialCase s_parallelSum = DiffCases.Create(
        StackLinqDifferentialKernels.LaunchParallelSumStackLinqKernel,
        inputs: static (rng, shape) =>
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
        shape: LaunchShapes.Grid1D(gridX: 1, blockX: 64, rows: 64),
        inputName: "xs",
        outputName: "r",
        reference: static (b, s) =>
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
        });

    [SkippableFact]
    public void StackLinq_Int32Matrix_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_int32Matrix);

    [SkippableFact]
    public void StackLinq_Int32Average_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_int32Average);

    [SkippableFact]
    public void StackLinq_SingleFusedAverage_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singleFusedAverage);

    [SkippableFact]
    public void StackLinq_SingleUnfusedAverage_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singleUnfusedAverage);

    [SkippableFact]
    public void StackLinq_FloatingExtremaAndZeroContains_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_floatingExtremaAndZeroContains);

    [SkippableFact]
    public void StackLinq_ContainsNaN_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_containsNaN);

    [SkippableFact]
    public void StackLinq_ContainsEvaluationOrder_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_containsEvaluationOrder);

    [SkippableFact]
    public void StackLinq_UnfusedSelectEvaluation_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_unfusedSelectEvaluation);

    [SkippableFact]
    public void StackLinq_NarrowAndInt64_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_narrowAndInt64);

    [SkippableFact]
    public void StackLinq_FloatSequentialSum_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_floatSequentialSum);

    [SkippableFact]
    public void StackLinq_DoubleSequentialSum_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_doubleSequentialSum);

    [SkippableFact]
    public void StackLinq_FusedFirst_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_fusedFirst);

    [SkippableFact]
    public void StackLinq_FusedFirstPredicate_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_fusedFirstPred);

    [SkippableFact]
    public void StackLinq_FusedLast_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_fusedLast);

    [SkippableFact]
    public void StackLinq_FusedLastPredicate_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_fusedLastPred);

    [SkippableFact]
    public void StackLinq_SingleOneElement_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_singleOneElem);

    [SkippableFact]
    public void StackLinq_SinglePredicateUnique_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singlePredUnique);

    [SkippableFact]
    public void StackLinq_SingleOrDefaultEmpty_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singleOrDefaultEmpty);

    [SkippableFact]
    public void StackLinq_SingleOrDefaultOneElement_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singleOrDefaultOneElem);

    [SkippableFact]
    public void StackLinq_SingleOrDefaultPredicateNone_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singleOrDefaultPredNone);

    [SkippableFact]
    public void StackLinq_ElementAtZero_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_elementAt0);

    [SkippableFact]
    public void StackLinq_ElementAtTwo_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_elementAt2);

    [SkippableFact]
    public void StackLinq_TakeWhileSideEffects_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_takeWhileSideEffects);

    [SkippableFact]
    public void StackLinq_SkipWhileSideEffects_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_skipWhileSideEffects);

    [SkippableFact]
    public void StackLinq_ParallelSum_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_parallelSum);



    private static int[] BuildInt32Slices(DiffRandom rng, int rows)
    {
        var values = DiffValues.Int32(rng, rows * SliceLen);
        ShrinkToSummable(values);
        if (rng.IsBoundarySeed)
        {
            for (int r = 0; r < s_int32Seed0Rows.Length && r < rows; r++)
            {
                Array.Copy(s_int32Seed0Rows[r], 0, values, r * SliceLen, SliceLen);
            }
        }

        return values;
    }

    // Keeps the sign and the varied bits, and no sum of the elements of a slice leaves the range of int32
    private static void ShrinkToSummable(int[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] >>= 6;
        }
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
