#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests (<see cref="Math"/>, <see cref="MathF"/>, <see cref="float"/> and <see cref="double"/>
/// intrinsics).
/// </summary>
public class MathDifferentialTests
{
    private static readonly float[] s_singleRoundBoundaries =
    [
        .. DiffValues.SingleBoundaries,
        -1.5f, 3.5f, -3.5f, 2f, 3f, 8388607.5f, -8388607.5f, 16777216f,
        MathF.BitDecrement(0.5f), MathF.BitIncrement(-0.5f), MathF.BitDecrement(-0.5f),
    ];

    private static readonly double[] s_doubleBoundaries =
    [
        0.0, -0.0, 1.0, -1.0, 2.0, -2.0, 3.0, -3.0, 0.25, -0.25,
        0.5, -0.5, 1.5, -1.5, 2.5, -2.5, 3.5, -3.5,
        Math.BitDecrement(0.5), Math.BitIncrement(-0.5), Math.BitDecrement(-0.5),
        double.Epsilon, -double.Epsilon, 2.2250738585072014e-308,
        double.MaxValue, double.MinValue, double.PositiveInfinity, double.NegativeInfinity, double.NaN,
        4503599627370495.5, -4503599627370495.5, 4503599627370496.0, 9007199254740992.0, 1e15 + 0.5,
    ];

    // Excludes int.MinValue so Math.Abs(int) does not throw OverflowException in the C# oracle (#947)
    private static readonly int[] s_intSafeAbsBoundaries =
    [
        0, 1, -1, 2, -2, 3, -3, int.MaxValue, int.MaxValue - 1, int.MinValue + 1,
        16777216, 16777217, -16777217, 16777219, -16777219,
        0x7FFF, -0x7FFF, 0x8000, -0x8000, 0xFFFF, 0x10000, 0x40000000, -0x40000000,
    ];

    private static readonly short[] s_shortSafeAbsBoundaries =
    [
        0, 1, -1, 2, -2, short.MaxValue, short.MaxValue - 1, short.MinValue + 1, 127, -127, 128, -128, 255, -255,
    ];

    private static readonly sbyte[] s_sbyteSafeAbsBoundaries =
    [
        0, 1, -1, 2, -2, sbyte.MaxValue, sbyte.MaxValue - 1, sbyte.MinValue + 1, 64, -64,
    ];

    private static readonly DifferentialCase s_mathOfSingle = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.MathOfSingleMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(s_singleRoundBoundaries.Length * 4, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("f", SingleRoundValues(rng, rows))
            .Out<double>("r", rows, MathDifferentialKernels.MathOfSingleColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => MathDifferentialKernels.LaunchMathOfSingleMatrixKernel(
            b.Get<float>("f"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => MathDifferentialKernels.MathOfSingleMatrixKernel(b.Get<float>("f"), b.Get<double>("r")),
    };

    private static readonly DifferentialCase s_singleRoundingAndUnary = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.SingleRoundingAndUnaryMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(s_singleRoundBoundaries.Length * 4, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("f", SingleRoundValues(rng, rows))
            .Out<float>("r", rows, MathDifferentialKernels.SingleRoundingAndUnaryColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => MathDifferentialKernels.LaunchSingleRoundingAndUnaryMatrixKernel(
            b.Get<float>("f"),
            b.Get<float>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => MathDifferentialKernels.SingleRoundingAndUnaryMatrixKernel(b.Get<float>("f"), b.Get<float>("r")),
    };

    private static readonly DifferentialCase s_doubleRoundingAndUnary = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.DoubleRoundingAndUnaryMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(s_doubleBoundaries.Length * 4, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("d", DoubleValues(rng, rows))
            .Out<double>("r", rows, MathDifferentialKernels.DoubleRoundingAndUnaryColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => MathDifferentialKernels.LaunchDoubleRoundingAndUnaryMatrixKernel(
            b.Get<double>("d"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => MathDifferentialKernels.DoubleRoundingAndUnaryMatrixKernel(b.Get<double>("d"), b.Get<double>("r")),
    };

    private static readonly DifferentialCase s_singleMaxMinClamp = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.SingleMaxMinClampMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(DiffValues.SingleBoundaries.Length * DiffValues.SingleBoundaries.Length, blockX: 64),
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
        Oracle = static b => MathDifferentialKernels.SingleMaxMinClampMatrixKernel(
            b.Get<float>("a"),
            b.Get<float>("b"),
            b.Get<float>("min"),
            b.Get<float>("max"),
            b.Get<float>("r")
        ),
    };

    private static readonly DifferentialCase s_doubleMaxMinClamp = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.DoubleMaxMinClampMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(s_doubleBoundaries.Length * s_doubleBoundaries.Length, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (a, b) = DoublePairs(rng, rows);
            var (min, max) = OrderedBounds(rng, rows, b, s_doubleBoundaries, NextDouble);
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
        Oracle = static b => MathDifferentialKernels.DoubleMaxMinClampMatrixKernel(
            b.Get<double>("a"),
            b.Get<double>("b"),
            b.Get<double>("min"),
            b.Get<double>("max"),
            b.Get<double>("r")
        ),
    };

    private static readonly DifferentialCase s_mixedFloatMaxMin = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.MixedFloatMaxMinMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(DiffValues.SingleBoundaries.Length * s_doubleBoundaries.Length, blockX: 64),
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
        Oracle = static b => MathDifferentialKernels.MixedFloatMaxMinMatrixKernel(
            b.Get<float>("f"),
            b.Get<double>("d"),
            b.Get<int>("i"),
            b.Get<float>("rf"),
            b.Get<double>("rd")
        ),
    };

    private static readonly DifferentialCase s_int32Math = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.Int32MathMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(s_intSafeAbsBoundaries.Length * s_intSafeAbsBoundaries.Length, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (a, b) = SafeAbsInt32Pairs(rng, rows);
            var s = SafeAbsInt16Values(rng, rows);
            var sb = SafeAbsSByteValues(rng, rows);
            var (min, max) = OrderedBounds(rng, rows, b, s_intSafeAbsBoundaries, NextSafeAbsInt32);
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
        Oracle = static b => MathDifferentialKernels.Int32MathMatrixKernel(
            b.Get<int>("a"),
            b.Get<int>("b"),
            b.Get<short>("s"),
            b.Get<sbyte>("sb"),
            b.Get<int>("min"),
            b.Get<int>("max"),
            b.Get<int>("r")
        ),
    };

    private static readonly DifferentialCase s_mathFIntArgument = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.MathFIntArgumentMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(s_intSafeAbsBoundaries.Length * 4, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (v, rawBound) = SafeAbsInt32Pairs(rng, rows);
            var (min, max) = OrderedBounds(rng, rows, rawBound, s_intSafeAbsBoundaries, NextSafeAbsInt32);
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
        Oracle = static b => MathDifferentialKernels.MathFIntArgumentMatrixKernel(
            b.Get<int>("v"),
            b.Get<int>("min"),
            b.Get<int>("max"),
            b.Get<float>("rf")
        ),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MathOfSingle_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_mathOfSingle);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SingleRoundingAndUnary_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_singleRoundingAndUnary);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DoubleRoundingAndUnary_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_doubleRoundingAndUnary);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SingleMaxMinClamp_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_singleMaxMinClamp);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DoubleMaxMinClamp_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_doubleMaxMinClamp);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MixedFloatMaxMin_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_mixedFloatMaxMin);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Int32Math_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_int32Math);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MathFIntArgument_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_mathFIntArgument);

    private static float[] SingleRoundValues(DiffRandom rng, int count) =>
        DiffValues.Mixed(rng, count, s_singleRoundBoundaries, DiffValues.NextSingle);

    private static double[] DoubleValues(DiffRandom rng, int count) =>
        DiffValues.Values(rng, count, s_doubleBoundaries, NextDouble);

    private static double NextDouble(DiffRandom rng)
    {
        if (rng.NextBool())
        {
            return s_doubleBoundaries[rng.NextBelow(s_doubleBoundaries.Length)];
        }

        if (rng.NextBool())
        {
            return BitConverter.UInt64BitsToDouble(rng.NextUInt64());
        }

        ulong sign = rng.NextBool() ? 0x8000000000000000ul : 0ul;
        ulong exponent = (ulong)(1023 - 40 + rng.NextBelow(101));
        ulong mantissa = rng.NextUInt64() >> 12;
        return BitConverter.UInt64BitsToDouble(sign | (exponent << 52) | mantissa);
    }

    private static (float[] Left, float[] Right) SinglePairs(DiffRandom rng, int rows) =>
        DiffValues.Pairs(rng, rows, DiffValues.SingleBoundaries, DiffValues.NextSingle);

    private static (double[] Left, double[] Right) DoublePairs(DiffRandom rng, int rows) =>
        DiffValues.Pairs(rng, rows, s_doubleBoundaries, NextDouble);

    private static (float[] Single, double[] Double) SingleDoublePairs(DiffRandom rng, int rows)
    {
        var single = new float[rows];
        var wide = new double[rows];
        int nf = DiffValues.SingleBoundaries.Length;
        int nd = s_doubleBoundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            if (rng.IsBoundarySeed)
            {
                single[r] = DiffValues.SingleBoundaries[r / nd % nf];
                wide[r] = s_doubleBoundaries[r % nd];
            }
            else
            {
                single[r] = DiffValues.NextSingle(rng);
                wide[r] = NextDouble(rng);
            }
        }

        return (single, wide);
    }

    // The second bound of each row (a boundary on seed 0, otherwise next), and the two sorted into (min, max)
    private static (T[] Min, T[] Max) OrderedBounds<T>(
        DiffRandom rng,
        int rows,
        T[] firstBound,
        T[] boundaries,
        Func<DiffRandom, T> next
    )
        where T : IComparisonOperators<T, T, bool>
    {
        var min = new T[rows];
        var max = new T[rows];
        int n = boundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            T lo = firstBound[r];
            T hi = rng.IsBoundarySeed ? boundaries[(r * 7 + 3) % n] : next(rng);
            if (lo > hi)
            {
                (lo, hi) = (hi, lo);
            }

            min[r] = lo;
            max[r] = hi;
        }

        return (min, max);
    }

    private static int NextSafeAbsInt32(DiffRandom rng)
    {
        if (rng.NextBool())
        {
            return s_intSafeAbsBoundaries[rng.NextBelow(s_intSafeAbsBoundaries.Length)];
        }

        int v = rng.NextInt32();
        return v == int.MinValue ? int.MinValue + 1 : v;
    }

    private static int[] SafeAbsInt32Values(DiffRandom rng, int rows) =>
        DiffValues.Values(rng, rows, s_intSafeAbsBoundaries, NextSafeAbsInt32);

    private static (int[] Left, int[] Right) SafeAbsInt32Pairs(DiffRandom rng, int rows) =>
        DiffValues.Pairs(rng, rows, s_intSafeAbsBoundaries, NextSafeAbsInt32);

    // short.MinValue is left out: Math.Abs of it throws in C#
    private static short[] SafeAbsInt16Values(DiffRandom rng, int rows) =>
        DiffValues.Mixed(rng, rows, s_shortSafeAbsBoundaries, static r =>
        {
            short v = unchecked((short)r.NextInt32());
            return v == short.MinValue ? (short)(short.MinValue + 1) : v;
        });

    // sbyte.MinValue is left out: Math.Abs of it throws in C#
    private static sbyte[] SafeAbsSByteValues(DiffRandom rng, int rows) =>
        DiffValues.Mixed(rng, rows, s_sbyteSafeAbsBoundaries, static r =>
        {
            sbyte v = unchecked((sbyte)r.NextInt32());
            return v == sbyte.MinValue ? (sbyte)(sbyte.MinValue + 1) : v;
        });
}
