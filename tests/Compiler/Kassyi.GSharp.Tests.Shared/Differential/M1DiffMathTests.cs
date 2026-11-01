#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests (<see cref="Math"/>, <see cref="MathF"/>, <see cref="float"/> and <see cref="double"/>
/// intrinsics).
/// </summary>
public class M1DiffMathTests
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

    private static readonly DifferentialCase s_mathOfSingle = new("M1DiffMathKernels.MathOfSingleMatrixKernel")
    {
        Shape = LaunchShape.ForRows(s_singleRoundBoundaries.Length * 4, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("f", SingleRoundValues(rng, rows))
            .Out<double>("r", rows, M1DiffMathKernels.MathOfSingleColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => M1DiffMathKernels.LaunchMathOfSingleMatrixKernel(
            b.Get<float>("f"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffMathKernels.MathOfSingleMatrixKernel(b.Get<float>("f"), b.Get<double>("r")),
    };

    private static readonly DifferentialCase s_singleRoundingAndUnary = new("M1DiffMathKernels.SingleRoundingAndUnaryMatrixKernel")
    {
        Shape = LaunchShape.ForRows(s_singleRoundBoundaries.Length * 4, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("f", SingleRoundValues(rng, rows))
            .Out<float>("r", rows, M1DiffMathKernels.SingleRoundingAndUnaryColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => M1DiffMathKernels.LaunchSingleRoundingAndUnaryMatrixKernel(
            b.Get<float>("f"),
            b.Get<float>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffMathKernels.SingleRoundingAndUnaryMatrixKernel(b.Get<float>("f"), b.Get<float>("r")),
    };

    private static readonly DifferentialCase s_doubleRoundingAndUnary = new("M1DiffMathKernels.DoubleRoundingAndUnaryMatrixKernel")
    {
        Shape = LaunchShape.ForRows(s_doubleBoundaries.Length * 4, blockX: 64),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("d", DoubleValues(rng, rows))
            .Out<double>("r", rows, M1DiffMathKernels.DoubleRoundingAndUnaryColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => M1DiffMathKernels.LaunchDoubleRoundingAndUnaryMatrixKernel(
            b.Get<double>("d"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffMathKernels.DoubleRoundingAndUnaryMatrixKernel(b.Get<double>("d"), b.Get<double>("r")),
    };

    private static readonly DifferentialCase s_singleMaxMinClamp = new("M1DiffMathKernels.SingleMaxMinClampMatrixKernel")
    {
        Shape = LaunchShape.ForRows(DiffValues.SingleBoundaries.Length * DiffValues.SingleBoundaries.Length, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (a, b) = SinglePairs(rng, rows);
            var (min, max) = OrderedSingleBounds(rng, rows, b);
            return new DiffBuffers()
                .In("a", a)
                .In("b", b)
                .In("min", min)
                .In("max", max)
                .Out<float>("r", rows, M1DiffMathKernels.SingleMaxMinClampColumns, OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => M1DiffMathKernels.LaunchSingleMaxMinClampMatrixKernel(
            b.Get<float>("a"),
            b.Get<float>("b"),
            b.Get<float>("min"),
            b.Get<float>("max"),
            b.Get<float>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffMathKernels.SingleMaxMinClampMatrixKernel(
            b.Get<float>("a"),
            b.Get<float>("b"),
            b.Get<float>("min"),
            b.Get<float>("max"),
            b.Get<float>("r")
        ),
    };

    private static readonly DifferentialCase s_doubleMaxMinClamp = new("M1DiffMathKernels.DoubleMaxMinClampMatrixKernel")
    {
        Shape = LaunchShape.ForRows(s_doubleBoundaries.Length * s_doubleBoundaries.Length, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (a, b) = DoublePairs(rng, rows);
            var (min, max) = OrderedDoubleBounds(rng, rows, b);
            return new DiffBuffers()
                .In("a", a)
                .In("b", b)
                .In("min", min)
                .In("max", max)
                .Out<double>("r", rows, M1DiffMathKernels.DoubleMaxMinClampColumns, OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => M1DiffMathKernels.LaunchDoubleMaxMinClampMatrixKernel(
            b.Get<double>("a"),
            b.Get<double>("b"),
            b.Get<double>("min"),
            b.Get<double>("max"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffMathKernels.DoubleMaxMinClampMatrixKernel(
            b.Get<double>("a"),
            b.Get<double>("b"),
            b.Get<double>("min"),
            b.Get<double>("max"),
            b.Get<double>("r")
        ),
    };

    private static readonly DifferentialCase s_mixedFloatMaxMin = new("M1DiffMathKernels.MixedFloatMaxMinMatrixKernel")
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
                .Out<float>("rf", rows, M1DiffMathKernels.MixedSingleMaxMinColumns, OutputPolicy.Bitwise)
                .Out<double>("rd", rows, M1DiffMathKernels.MixedDoubleMaxMinColumns, OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => M1DiffMathKernels.LaunchMixedFloatMaxMinMatrixKernel(
            b.Get<float>("f"),
            b.Get<double>("d"),
            b.Get<int>("i"),
            b.Get<float>("rf"),
            b.Get<double>("rd"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffMathKernels.MixedFloatMaxMinMatrixKernel(
            b.Get<float>("f"),
            b.Get<double>("d"),
            b.Get<int>("i"),
            b.Get<float>("rf"),
            b.Get<double>("rd")
        ),
    };

    private static readonly DifferentialCase s_int32Math = new("M1DiffMathKernels.Int32MathMatrixKernel")
    {
        Shape = LaunchShape.ForRows(s_intSafeAbsBoundaries.Length * s_intSafeAbsBoundaries.Length, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (a, b) = SafeAbsInt32Pairs(rng, rows);
            var s = SafeAbsInt16Values(rng, rows);
            var sb = SafeAbsSByteValues(rng, rows);
            var (min, max) = OrderedInt32Bounds(rng, rows, b);
            return new DiffBuffers()
                .In("a", a)
                .In("b", b)
                .In("s", s)
                .In("sb", sb)
                .In("min", min)
                .In("max", max)
                .Out<int>("r", rows, M1DiffMathKernels.Int32MathColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => M1DiffMathKernels.LaunchInt32MathMatrixKernel(
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
        Oracle = static b => M1DiffMathKernels.Int32MathMatrixKernel(
            b.Get<int>("a"),
            b.Get<int>("b"),
            b.Get<short>("s"),
            b.Get<sbyte>("sb"),
            b.Get<int>("min"),
            b.Get<int>("max"),
            b.Get<int>("r")
        ),
    };

    private static readonly DifferentialCase s_mathFIntArgument = new("M1DiffMathKernels.MathFIntArgumentMatrixKernel")
    {
        Shape = LaunchShape.ForRows(s_intSafeAbsBoundaries.Length * 4, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (v, rawBound) = SafeAbsInt32Pairs(rng, rows);
            var (min, max) = OrderedInt32Bounds(rng, rows, rawBound);
            return new DiffBuffers()
                .In("v", v)
                .In("min", min)
                .In("max", max)
                .Out<float>("rf", rows, M1DiffMathKernels.MathFIntSingleColumns, OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => M1DiffMathKernels.LaunchMathFIntArgumentMatrixKernel(
            b.Get<int>("v"),
            b.Get<int>("min"),
            b.Get<int>("max"),
            b.Get<float>("rf"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => M1DiffMathKernels.MathFIntArgumentMatrixKernel(
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

    private static float[] SingleRoundValues(DiffRandom rng, int count)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = rng.IsBoundarySeed
                ? s_singleRoundBoundaries[i % s_singleRoundBoundaries.Length]
                : (rng.NextBool() ? s_singleRoundBoundaries[rng.NextBelow(s_singleRoundBoundaries.Length)] : DiffValues.NextSingle(rng));
        }

        return values;
    }

    private static double[] DoubleValues(DiffRandom rng, int count)
    {
        var values = new double[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = rng.IsBoundarySeed ? s_doubleBoundaries[i % s_doubleBoundaries.Length] : NextDouble(rng);
        }

        return values;
    }

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

    private static (float[] Left, float[] Right) SinglePairs(DiffRandom rng, int rows)
    {
        var left = new float[rows];
        var right = new float[rows];
        int n = DiffValues.SingleBoundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            if (rng.IsBoundarySeed)
            {
                left[r] = DiffValues.SingleBoundaries[r / n % n];
                right[r] = DiffValues.SingleBoundaries[r % n];
            }
            else
            {
                left[r] = DiffValues.NextSingle(rng);
                right[r] = DiffValues.NextSingle(rng);
            }
        }

        return (left, right);
    }

    private static (double[] Left, double[] Right) DoublePairs(DiffRandom rng, int rows)
    {
        var left = new double[rows];
        var right = new double[rows];
        int n = s_doubleBoundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            if (rng.IsBoundarySeed)
            {
                left[r] = s_doubleBoundaries[r / n % n];
                right[r] = s_doubleBoundaries[r % n];
            }
            else
            {
                left[r] = NextDouble(rng);
                right[r] = NextDouble(rng);
            }
        }

        return (left, right);
    }

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

    private static (float[] Min, float[] Max) OrderedSingleBounds(DiffRandom rng, int rows, float[] firstBound)
    {
        var min = new float[rows];
        var max = new float[rows];
        int n = DiffValues.SingleBoundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            float lo = firstBound[r];
            float hi = rng.IsBoundarySeed ? DiffValues.SingleBoundaries[(r * 7 + 3) % n] : DiffValues.NextSingle(rng);
            if (lo > hi)
            {
                (lo, hi) = (hi, lo);
            }

            min[r] = lo;
            max[r] = hi;
        }

        return (min, max);
    }

    private static (double[] Min, double[] Max) OrderedDoubleBounds(DiffRandom rng, int rows, double[] firstBound)
    {
        var min = new double[rows];
        var max = new double[rows];
        int n = s_doubleBoundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            double lo = firstBound[r];
            double hi = rng.IsBoundarySeed ? s_doubleBoundaries[(r * 7 + 3) % n] : NextDouble(rng);
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

    private static int[] SafeAbsInt32Values(DiffRandom rng, int rows)
    {
        var values = new int[rows];
        int n = s_intSafeAbsBoundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            values[r] = rng.IsBoundarySeed ? s_intSafeAbsBoundaries[r % n] : NextSafeAbsInt32(rng);
        }

        return values;
    }

    private static (int[] Left, int[] Right) SafeAbsInt32Pairs(DiffRandom rng, int rows)
    {
        var left = new int[rows];
        var right = new int[rows];
        int n = s_intSafeAbsBoundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            if (rng.IsBoundarySeed)
            {
                left[r] = s_intSafeAbsBoundaries[r / n % n];
                right[r] = s_intSafeAbsBoundaries[r % n];
            }
            else
            {
                left[r] = NextSafeAbsInt32(rng);
                right[r] = NextSafeAbsInt32(rng);
            }
        }

        return (left, right);
    }

    private static (int[] Min, int[] Max) OrderedInt32Bounds(DiffRandom rng, int rows, int[] firstBound)
    {
        var min = new int[rows];
        var max = new int[rows];
        int n = s_intSafeAbsBoundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            int lo = firstBound[r];
            int hi = rng.IsBoundarySeed ? s_intSafeAbsBoundaries[(r * 7 + 3) % n] : NextSafeAbsInt32(rng);
            if (lo > hi)
            {
                (lo, hi) = (hi, lo);
            }

            min[r] = lo;
            max[r] = hi;
        }

        return (min, max);
    }

    private static short[] SafeAbsInt16Values(DiffRandom rng, int rows)
    {
        var values = new short[rows];
        int n = s_shortSafeAbsBoundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            if (rng.IsBoundarySeed || rng.NextBool())
            {
                values[r] = s_shortSafeAbsBoundaries[rng.IsBoundarySeed ? r % n : rng.NextBelow(n)];
            }
            else
            {
                short v = unchecked((short)rng.NextInt32());
                values[r] = v == short.MinValue ? (short)(short.MinValue + 1) : v;
            }
        }

        return values;
    }

    private static sbyte[] SafeAbsSByteValues(DiffRandom rng, int rows)
    {
        var values = new sbyte[rows];
        int n = s_sbyteSafeAbsBoundaries.Length;
        for (int r = 0; r < rows; r++)
        {
            if (rng.IsBoundarySeed || rng.NextBool())
            {
                values[r] = s_sbyteSafeAbsBoundaries[rng.IsBoundarySeed ? r % n : rng.NextBelow(n)];
            }
            else
            {
                sbyte v = unchecked((sbyte)rng.NextInt32());
                values[r] = v == sbyte.MinValue ? (sbyte)(sbyte.MinValue + 1) : v;
            }
        }

        return values;
    }
}
