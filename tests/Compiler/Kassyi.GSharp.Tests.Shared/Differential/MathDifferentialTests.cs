#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.MathDifferentialInputs;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests (<see cref="Math"/>, <see cref="MathF"/>, <see cref="BitOperations"/>,
/// <see cref="float"/> and <see cref="double"/> rounding, comparisons, subnormals, and FMA contraction).
/// </summary>
public partial class MathDifferentialTests
{
    internal static readonly DifferentialCase s_mathOfSingle =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.MathOfSingleMatrixKernel)}")
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(SingleRoundBoundaries.Length * 4),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("f", SingleRoundValues(rng, rows))
                .Out<double>("r", rows, MathDifferentialKernels.MathOfSingleColumns, OutputPolicy.Bitwise),
            Launch = static (b, s) => MathDifferentialKernels.LaunchMathOfSingleMatrixKernel(
                b.Get<float>("f"),
                b.Get<double>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
        };

    internal static readonly DifferentialCase s_singleRoundingAndUnary =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.SingleRoundingAndUnaryMatrixKernel)}")
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(SingleRoundBoundaries.Length * 4),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("f", SingleRoundValues(rng, rows))
                .Out<float>("r", rows, MathDifferentialKernels.SingleRoundingAndUnaryColumns, OutputPolicy.Bitwise),
            Launch = static (b, s) => MathDifferentialKernels.LaunchSingleRoundingAndUnaryMatrixKernel(
                b.Get<float>("f"),
                b.Get<float>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
        };

    internal static readonly DifferentialCase s_doubleRoundingAndUnary =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.DoubleRoundingAndUnaryMatrixKernel)}")
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(DoubleBoundaries.Length * 4),
            Inputs = static (rng, rows) => new DiffBuffers()
                .In("d", DoubleValues(rng, rows))
                .Out<double>("r", rows, MathDifferentialKernels.DoubleRoundingAndUnaryColumns, OutputPolicy.Bitwise),
            Launch = static (b, s) => MathDifferentialKernels.LaunchDoubleRoundingAndUnaryMatrixKernel(
                b.Get<double>("d"),
                b.Get<double>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
        };

    internal static readonly DifferentialCase s_singleMaxMinClamp =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.SingleMaxMinClampMatrixKernel)}")
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(
                DiffValues.SingleBoundaries.Length * DiffValues.SingleBoundaries.Length
            ),
            Inputs = static (rng, rows) =>
            {
                var (a, b) = SinglePairs(rng, rows);
                var (min, max) = OrderedBounds(rng, rows, b, DiffValues.SingleBoundaries, DiffValues.NextSingle);
                return new DiffBuffers()
                    .In("a", a)
                    .In("b", b)
                    .In("min", min)
                    .In("max", max)
                    .Out<float>("r", rows, MathDifferentialKernels.SingleMaxMinClampColumns, OutputPolicy.Bitwise);
            },
            Launch = static (b, s) => MathDifferentialKernels.LaunchSingleMaxMinClampMatrixKernel(
                b.Get<float>("a"),
                b.Get<float>("b"),
                b.Get<float>("min"),
                b.Get<float>("max"),
                b.Get<float>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
        };

    internal static readonly DifferentialCase s_doubleMaxMinClamp =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.DoubleMaxMinClampMatrixKernel)}")
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(DoubleBoundaries.Length * DoubleBoundaries.Length),
            Inputs = static (rng, rows) =>
            {
                var (a, b) = DoublePairs(rng, rows);
                var (min, max) = OrderedBounds(rng, rows, b, DoubleBoundaries, NextDouble);
                return new DiffBuffers()
                    .In("a", a)
                    .In("b", b)
                    .In("min", min)
                    .In("max", max)
                    .Out<double>("r", rows, MathDifferentialKernels.DoubleMaxMinClampColumns, OutputPolicy.Bitwise);
            },
            Launch = static (b, s) => MathDifferentialKernels.LaunchDoubleMaxMinClampMatrixKernel(
                b.Get<double>("a"),
                b.Get<double>("b"),
                b.Get<double>("min"),
                b.Get<double>("max"),
                b.Get<double>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
        };

    internal static readonly DifferentialCase s_mixedFloatMaxMin =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.MixedFloatMaxMinMatrixKernel)}")
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(DiffValues.SingleBoundaries.Length * DoubleBoundaries.Length),
            Inputs = static (rng, rows) =>
            {
                var (f, d) = SingleDoublePairs(rng, rows);
                var i = SafeAbsInt32Values(rng, rows);
                return new DiffBuffers()
                    .In("f", f)
                    .In("d", d)
                    .In("i", i)
                    .Out<float>("rf", rows, MathDifferentialKernels.MixedSingleMaxMinColumns, OutputPolicy.Bitwise)
                    .Out<double>("rd", rows, MathDifferentialKernels.MixedDoubleMaxMinColumns, OutputPolicy.Bitwise);
            },
            Launch = static (b, s) => MathDifferentialKernels.LaunchMixedFloatMaxMinMatrixKernel(
                b.Get<float>("f"),
                b.Get<double>("d"),
                b.Get<int>("i"),
                b.Get<float>("rf"),
                b.Get<double>("rd"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
        };

    internal static readonly DifferentialCase s_int32Math =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.Int32MathMatrixKernel)}")
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(IntSafeAbsBoundaries.Length * IntSafeAbsBoundaries.Length),
            Inputs = static (rng, rows) =>
            {
                var (a, b) = SafeAbsInt32Pairs(rng, rows);
                var s = SafeAbsInt16Values(rng, rows);
                var sb = SafeAbsSByteValues(rng, rows);
                var (min, max) = OrderedBounds(rng, rows, b, IntSafeAbsBoundaries, NextSafeAbsInt32);
                return new DiffBuffers()
                    .In("a", a)
                    .In("b", b)
                    .In("s", s)
                    .In("sb", sb)
                    .In("min", min)
                    .In("max", max)
                    .Out<int>("r", rows, MathDifferentialKernels.Int32MathColumns, OutputPolicy.Exact);
            },
            Launch = static (b, s) => MathDifferentialKernels.LaunchInt32MathMatrixKernel(
                b.Get<int>("a"),
                b.Get<int>("b"),
                b.Get<short>("s"),
                b.Get<sbyte>("sb"),
                b.Get<int>("min"),
                b.Get<int>("max"),
                b.Get<int>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
        };

    internal static readonly DifferentialCase s_mathFIntArgument =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.MathFIntArgumentMatrixKernel)}")
        {
            Shapes = LaunchShapes.OneDimensionalMatrix(IntSafeAbsBoundaries.Length * 4),
            Inputs = static (rng, rows) =>
            {
                var (v, rawBound) = SafeAbsInt32Pairs(rng, rows);
                var (min, max) = OrderedBounds(rng, rows, rawBound, IntSafeAbsBoundaries, NextSafeAbsInt32);
                return new DiffBuffers()
                    .In("v", v)
                    .In("min", min)
                    .In("max", max)
                    .Out<float>("rf", rows, MathDifferentialKernels.MathFIntSingleColumns, OutputPolicy.Bitwise);
            },
            Launch = static (b, s) => MathDifferentialKernels.LaunchMathFIntArgumentMatrixKernel(
                b.Get<int>("v"),
                b.Get<int>("min"),
                b.Get<int>("max"),
                b.Get<float>("rf"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
        };

    // Subnormal IEEE 754 0-ulp arithmetic and Sqrt without FTZ
    internal static readonly DifferentialCase s_subnormalIeeeExact =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.SubnormalIeeeExactMatrixKernel)}")
        {
            Shape = LaunchShape.ForRows(
                DiffValues.SubnormalSingleBoundaries.Length * DiffValues.SubnormalSingleBoundaries.Length,
                blockX: 64
            ),
            Inputs = static (rng, rows) =>
            {
                var (fa, fb) = DiffValues.Pairs(rng, rows, DiffValues.SubnormalSingleBoundaries, NextSubnormalSingle);
                var (da, db) = DiffValues.Pairs(rng, rows, DiffValues.SubnormalDoubleBoundaries, NextSubnormalDouble);
                return new DiffBuffers()
                    .In("fa", fa)
                    .In("fb", fb)
                    .In("da", da)
                    .In("db", db)
                    .Out<float>("rf", rows, MathDifferentialKernels.SubnormalIeeeSingleColumns, OutputPolicy.Bitwise)
                    .Out<double>("rd", rows, MathDifferentialKernels.SubnormalIeeeDoubleColumns, OutputPolicy.Bitwise);
            },
            Launch = static (b, s) => MathDifferentialKernels.LaunchSubnormalIeeeExactMatrixKernel(
                b.Get<float>("fa"),
                b.Get<float>("fb"),
                b.Get<double>("da"),
                b.Get<double>("db"),
                b.Get<float>("rf"),
                b.Get<double>("rd"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
        };

    // System.Numerics.BitOperations all 18 mapped overloads + shift count masking + named arguments
    internal static readonly DifferentialCase s_bitOperations =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.BitOperationsMatrixKernel)}")
        {
            Shape = LaunchShape.ForRows(
                DiffValues.BitOperationUInt32Boundaries.Length * DiffValues.ShiftCounts.Length,
                blockX: 64
            ),
            Inputs = static (rng, rows) =>
            {
                var u32 = new uint[rows];
                var u64 = new ulong[rows];
                var offsets = new int[rows];
                int nVal = DiffValues.BitOperationUInt32Boundaries.Length;
                int nVal64 = DiffValues.BitOperationUInt64Boundaries.Length;
                int nShift = DiffValues.ShiftCounts.Length;

                for (int r = 0; r < rows; r++)
                {
                    if (rng.IsBoundarySeed)
                    {
                        u32[r] = DiffValues.BitOperationUInt32Boundaries[r / nShift % nVal];
                        u64[r] = DiffValues.BitOperationUInt64Boundaries[r / nShift % nVal64];
                        offsets[r] = DiffValues.ShiftCounts[r % nShift];
                    }
                    else
                    {
                        u32[r] = rng.NextBool()
                            ? DiffValues.BitOperationUInt32Boundaries[rng.NextBelow(nVal)]
                            : rng.NextUInt32();
                        u64[r] = rng.NextBool()
                            ? DiffValues.BitOperationUInt64Boundaries[rng.NextBelow(nVal64)]
                            : rng.NextUInt64();
                        offsets[r] = rng.NextBool()
                            ? DiffValues.ShiftCounts[rng.NextBelow(nShift)]
                            : rng.NextInt32();
                    }
                }

                // Guarantee the named-argument witness (v32 = 0x1234_5678u, s = 4) is present on every seed
                if (rows > 0)
                {
                    u32[0] = 0x1234_5678u;
                    u64[0] = 0x1234_5678_9ABC_DEF0UL;
                    offsets[0] = 4;
                }

                return new DiffBuffers()
                    .In("u32", u32, OutputPolicy.Exact)
                    .In("u64", u64, OutputPolicy.Exact)
                    .In("offsets", offsets, OutputPolicy.Exact)
                    .Out<ulong>("r", rows, MathDifferentialKernels.BitOperationsColumns, OutputPolicy.Exact);
            },
            Launch = static (b, s) => MathDifferentialKernels.LaunchBitOperationsMatrixKernel(
                b.Get<uint>("u32"),
                b.Get<ulong>("u64"),
                b.Get<int>("offsets"),
                b.Get<ulong>("r"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
        };

    // IEEE 754 unordered comparisons and large-exponent Round ties
    internal static readonly DifferentialCase s_floatComparisonAndLargeTie = new(
        $"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.FloatComparisonAndLargeTieMatrixKernel)}"
    )
    {
        Shape = LaunchShape.ForRows(SingleRoundBoundaries.Length * 8, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (fa, fb) = DiffValues.Pairs(rng, rows, SingleRoundBoundaries, DiffValues.NextSingle);
            var (da, db) = DiffValues.Pairs(rng, rows, DoubleBoundaries, NextDouble);
            var ia = SafeAbsInt32Values(rng, rows);

            if (rows > 2)
            {
                // Witness: 2^24 vs 2^24 + 1 (int -> float rounding in x < iv)
                fa[0] = 16777216.0f;
                fb[0] = -0.0f;
                da[0] = 4503599627370495.5;
                db[0] = -0.0;
                ia[0] = 16777217;

                // Witness: -float.Epsilon vs -0.0f and -0.5 round to -0.0
                fa[1] = -float.Epsilon;
                fb[1] = -0.0f;
                da[1] = -0.5;
                db[1] = 0.0;
                ia[1] = 0;
            }

            return new DiffBuffers()
                .In("fa", fa)
                .In("fb", fb)
                .In("da", da)
                .In("db", db)
                .In("ia", ia, OutputPolicy.Exact)
                .Out<byte>("rCmp", rows, MathDifferentialKernels.FloatComparisonColumns, OutputPolicy.Exact)
                .Out<double>("rRound", rows, MathDifferentialKernels.LargeTieRoundColumns, OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => MathDifferentialKernels.LaunchFloatComparisonAndLargeTieMatrixKernel(
            b.Get<float>("fa"),
            b.Get<float>("fb"),
            b.Get<double>("da"),
            b.Get<double>("db"),
            b.Get<int>("ia"),
            b.Get<byte>("rCmp"),
            b.Get<double>("rRound"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        OracleIsLaunch = true,
    };

    // Mul-Add exact inputs (Bitwise) and FMA contraction witness (Verify)
    internal static readonly DifferentialCase s_mulAddExactAndContraction =
        new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.MulAddExactAndContractionKernel)}")
        {
            Shape = LaunchShape.ForRows(128, blockX: 64),
            Inputs = static (rng, rows) =>
            {
                var fa = new float[rows];
                var fb = new float[rows];
                var fc = new float[rows];

                for (int i = 0; i < rows; i++)
                {
                    // fa has <= 12 mantissa bits and fb has <= 11 mantissa bits so fa * fb is exact in binary32
                    int ma = rng.IsBoundarySeed ? (i - 64) : (rng.NextBelow(2049) - 1024);
                    int mb = rng.IsBoundarySeed ? ((i * 3) % 65 - 32) : (rng.NextBelow(1025) - 512);
                    int mc = rng.IsBoundarySeed ? ((i * 7) % 129 - 64) : (rng.NextBelow(2049) - 1024);
                    fa[i] = ma * 0.0625f;
                    fb[i] = mb * 0.125f;
                    fc[i] = mc * 0.25f;
                }

                if (rows > 0)
                {
                    // Row 0: Kahan FMA contraction witness k = 1 + 2^-12 (13 mantissa bits), fb = 1.0f (1 mantissa bit)
                    fa[0] = 1.000244140625f;
                    fb[0] = 1.0f;
                    fc[0] = -1.0f;
                }

                return new DiffBuffers()
                    .In("fa", fa)
                    .In("fb", fb)
                    .In("fc", fc)
                    .Out<float>("rExact", rows, OutputPolicy.Bitwise)
                    .Out<float>("rContract", rows, OutputPolicy.Ignore);
            },
            Launch = static (b, s) => MathDifferentialKernels.LaunchMulAddExactAndContractionKernel(
                b.Get<float>("fa"),
                b.Get<float>("fb"),
                b.Get<float>("fc"),
                b.Get<float>("rExact"),
                b.Get<float>("rContract"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX
            ),
            OracleIsLaunch = true,
            Verify = static (b, s) =>
            {
                var fa = b.Get<float>("fa");
                var rContract = b.Get<float>("rContract");
                const float c = 1.0f + 2.0f * 0.000244140625f;

                for (int i = 0; i < s.Rows; i++)
                {
                    float k = fa[i];
                    float fmaExpected = MathF.FusedMultiplyAdd(k, k, -c);
                    float uncontractedProduct = (float)((double)k * k);
                    float twoStepExpected = uncontractedProduct - c;
                    float actual = rContract[i];

                    int actualBits = BitConverter.SingleToInt32Bits(actual);
                    bool matchesFma = actualBits == BitConverter.SingleToInt32Bits(fmaExpected);
                    bool matchesTwoStep = actualBits == BitConverter.SingleToInt32Bits(twoStepExpected);
                    if (!matchesFma && !matchesTwoStep)
                    {
                        return PropertyResult.Fail(
                            $"row {i} (k={k}): rContract={actual} matches neither FMA ({fmaExpected}) "
                                + $"nor two-step ({twoStepExpected})"
                        );
                    }

                    var fmaFailure = CheckFmaContract(i, k, matchesFma, fmaExpected, actual);
                    if (fmaFailure is not null)
                    {
                        return fmaFailure.Value;
                    }
                }

                return PropertyResult.Pass();
            },
        };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_SinglePrecision_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_mathOfSingle);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_SingleRoundingAndUnary_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singleRoundingAndUnary);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_DoubleRoundingAndUnary_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_doubleRoundingAndUnary);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_SingleMaxMinClamp_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_singleMaxMinClamp);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_DoubleMaxMinClamp_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_doubleMaxMinClamp);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_MixedFloatMaxMin_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mixedFloatMaxMin);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_Int32Operations_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_int32Math);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_MathFIntArgument_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mathFIntArgument);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_SubnormalIeeeExact_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_subnormalIeeeExact);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void BitOperations_VariousInputs_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_bitOperations);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_FloatComparisonAndLargeTie_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_floatComparisonAndLargeTie);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void MathFunctions_MulAddExactAndContraction_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_mulAddExactAndContraction);

    private static partial PropertyResult? CheckFmaContract(
        int i,
        float k,
        bool matchesFma,
        float fmaExpected,
        float actual
    );
}
