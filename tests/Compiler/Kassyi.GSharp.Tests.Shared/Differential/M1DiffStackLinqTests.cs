#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public class M1DiffStackLinqTests
{
    private const int SliceLen = M1DiffStackLinqKernels.SliceLength;

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

    private static readonly DifferentialCase s_int32Matrix = new("M1DiffStackLinqKernels.Int32StackLinqMatrixKernel")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, M1DiffStackLinqKernels.Int32StackLinqColumns, OutputPolicy.Exact),
        Launch = static (b, s) => M1DiffStackLinqKernels.LaunchInt32StackLinqMatrixKernel(
            b.Get<int>("xs"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffStackLinqKernels.Int32StackLinqMatrixKernel(b.Get<int>("xs"), b.Get<long>("r")),
    };

    private static readonly DifferentialCase s_int32Average = new("M1DiffStackLinqKernels.Int32AverageKernel")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
            .Out<double>("r", rows, M1DiffStackLinqKernels.Int32AverageColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => M1DiffStackLinqKernels.LaunchInt32AverageKernel(
            b.Get<int>("xs"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffStackLinqKernels.Int32AverageKernel(b.Get<int>("xs"), b.Get<double>("r")),
    };

    private static readonly DifferentialCase s_singleFusedAverage = new("M1DiffStackLinqKernels.SingleFusedAverageKernel")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<double>("r", rows, M1DiffStackLinqKernels.SingleFusedAverageColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => M1DiffStackLinqKernels.LaunchSingleFusedAverageKernel(
            b.Get<float>("xs"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffStackLinqKernels.SingleFusedAverageKernel(b.Get<float>("xs"), b.Get<double>("r")),
    };

    private static readonly DifferentialCase s_singleUnfusedAverage = new("M1DiffStackLinqKernels.SingleUnfusedAverageKernel")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<double>("r", rows, M1DiffStackLinqKernels.SingleUnfusedAverageColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => M1DiffStackLinqKernels.LaunchSingleUnfusedAverageKernel(
            b.Get<float>("xs"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffStackLinqKernels.SingleUnfusedAverageKernel(b.Get<float>("xs"), b.Get<double>("r")),
    };

    private static readonly DifferentialCase s_floatingExtremaAndZeroContains = new("M1DiffStackLinqKernels.FloatingExtremaAndZeroContainsKernel")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("fs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .In("ds", BuildDoubleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<double>("r", rows, M1DiffStackLinqKernels.FloatingExtremaAndZeroContainsColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => M1DiffStackLinqKernels.LaunchFloatingExtremaAndZeroContainsKernel(
            b.Get<float>("fs"),
            b.Get<double>("ds"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffStackLinqKernels.FloatingExtremaAndZeroContainsKernel(
            b.Get<float>("fs"),
            b.Get<double>("ds"),
            b.Get<double>("r")
        ),
    };

    private static readonly DifferentialCase s_containsNaN = new("M1DiffStackLinqKernels.ContainsNaNKernel")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("fs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .In("ds", BuildDoubleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<long>("r", rows, M1DiffStackLinqKernels.ContainsNaNColumns, OutputPolicy.Exact),
        Launch = static (b, s) => M1DiffStackLinqKernels.LaunchContainsNaNKernel(
            b.Get<float>("fs"),
            b.Get<double>("ds"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffStackLinqKernels.ContainsNaNKernel(
            b.Get<float>("fs"),
            b.Get<double>("ds"),
            b.Get<long>("r")
        ),
    };

    private static readonly DifferentialCase s_containsEvaluationOrder = new("M1DiffStackLinqKernels.ContainsEvaluationOrderKernel")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, M1DiffStackLinqKernels.ContainsEvaluationOrderColumns, OutputPolicy.Exact),
        Launch = static (b, s) => M1DiffStackLinqKernels.LaunchContainsEvaluationOrderKernel(
            b.Get<int>("xs"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffStackLinqKernels.ContainsEvaluationOrderKernel(b.Get<int>("xs"), b.Get<long>("r")),
    };

    private static readonly DifferentialCase s_unfusedSelectEvaluation = new("M1DiffStackLinqKernels.UnfusedSelectEvaluationKernel")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildInt32Slices(rng, rows), OutputPolicy.Exact)
            .Out<long>("r", rows, M1DiffStackLinqKernels.UnfusedSelectEvaluationColumns, OutputPolicy.Exact),
        Launch = static (b, s) => M1DiffStackLinqKernels.LaunchUnfusedSelectEvaluationKernel(
            b.Get<int>("xs"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffStackLinqKernels.UnfusedSelectEvaluationKernel(b.Get<int>("xs"), b.Get<long>("r")),
    };

    private static readonly DifferentialCase s_narrowAndInt64 = new("M1DiffStackLinqKernels.NarrowAndInt64StackLinqKernel")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) =>
        {
            var (bs, ss, ls) = BuildNarrowAndInt64Slices(rng, rows);
            return new DiffBuffers()
                .In("bs", bs, OutputPolicy.Exact)
                .In("ss", ss, OutputPolicy.Exact)
                .In("ls", ls, OutputPolicy.Exact)
                .Out<long>("r", rows, M1DiffStackLinqKernels.NarrowAndInt64StackLinqColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffStackLinqKernels.LaunchNarrowAndInt64StackLinqKernel(
            b.Get<byte>("bs"),
            b.Get<short>("ss"),
            b.Get<long>("ls"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffStackLinqKernels.NarrowAndInt64StackLinqKernel(
            b.Get<byte>("bs"),
            b.Get<short>("ss"),
            b.Get<long>("ls"),
            b.Get<long>("r")
        ),
    };

    private static readonly DifferentialCase s_floatSequentialSum = new("M1DiffStackLinqKernels.FloatSequentialSumKernel")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("xs", BuildSingleSlices(rng, rows), OutputPolicy.Bitwise)
            .Out<float>("r", rows, M1DiffStackLinqKernels.FloatSequentialSumColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => M1DiffStackLinqKernels.LaunchFloatSequentialSumKernel(
            b.Get<float>("xs"),
            b.Get<float>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Reference = static b =>
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

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Int32StackLinqMatrix_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_int32Matrix);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Int32Average_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_int32Average);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SingleFusedAverage_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_singleFusedAverage);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SingleUnfusedAverage_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_singleUnfusedAverage);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_FloatingExtremaAndZeroContains_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_floatingExtremaAndZeroContains);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ContainsNaN_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_containsNaN);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ContainsEvaluationOrder_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_containsEvaluationOrder);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_UnfusedSelectEvaluation_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_unfusedSelectEvaluation);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NarrowAndInt64StackLinq_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_narrowAndInt64);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_FloatSequentialSum_MatchesTheReferenceOnEveryPath() => DifferentialRunner.Run(s_floatSequentialSum);

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
