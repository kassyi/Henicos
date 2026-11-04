#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests (<see cref="Math"/>, <see cref="MathF"/>, <see cref="FastMath"/>, <see cref="BitOperations"/>,
/// <see cref="float"/> and <see cref="double"/> intrinsics).
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
        double.Epsilon, -double.Epsilon,
        BitConverter.Int64BitsToDouble(0x000F_FFFF_FFFF_FFFFL),
        -BitConverter.Int64BitsToDouble(0x000F_FFFF_FFFF_FFFFL),
        2.2250738585072014e-308, -2.2250738585072014e-308,
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

    private static readonly float[] s_trigExpSpecialSingles =
    [
        0.0f, -0.0f, float.PositiveInfinity, float.NegativeInfinity, float.NaN,
    ];

    private static readonly float[] s_logSpecialSingles =
    [
        0.0f, -0.0f, 1.0f, -1.0f, float.PositiveInfinity, float.NegativeInfinity, float.NaN,
    ];

    private static readonly (float Base, float Exp)[] s_powSpecialSinglePairs =
    [
        (0.0f, 3.0f),
        (-0.0f, 3.0f),
        (0.0f, -3.0f),
        (-0.0f, -3.0f),
        (0.0f, 2.0f),
        (-0.0f, 2.0f),
        (0.0f, -2.0f),
        (-0.0f, -2.0f),
        (-1.0f, float.PositiveInfinity),
        (-1.0f, float.NegativeInfinity),
        (0.5f, float.PositiveInfinity),
        (0.5f, float.NegativeInfinity),
        (2.0f, float.PositiveInfinity),
        (2.0f, float.NegativeInfinity),
        (-2.0f, 3.0f),
        (-2.0f, 0.5f),
        (1.0f, float.NaN),
        (float.NaN, 0.0f),
        (float.NaN, -0.0f),
    ];

    private static readonly double[] s_trigExpSpecialDoubles =
    [
        0.0, -0.0, double.PositiveInfinity, double.NegativeInfinity, double.NaN,
    ];

    private static readonly double[] s_logSpecialDoubles =
    [
        0.0, -0.0, 1.0, -1.0, double.PositiveInfinity, double.NegativeInfinity, double.NaN,
    ];

    private static readonly (double Base, double Exp)[] s_powSpecialDoublePairs =
    [
        (0.0, 3.0),
        (-0.0, 3.0),
        (0.0, -3.0),
        (-0.0, -3.0),
        (0.0, 2.0),
        (-0.0, 2.0),
        (0.0, -2.0),
        (-0.0, -2.0),
        (-1.0, double.PositiveInfinity),
        (-1.0, double.NegativeInfinity),
        (0.5, double.PositiveInfinity),
        (0.5, double.NegativeInfinity),
        (2.0, double.PositiveInfinity),
        (2.0, double.NegativeInfinity),
        (-2.0, 3.0),
        (-2.0, 0.5),
        (1.0, double.NaN),
        (double.NaN, 0.0),
        (double.NaN, -0.0),
    ];

    private static readonly float[] s_trigDomainSingleBoundaries =
    [
        0.0f, -0.0f, float.Epsilon, -float.Epsilon, 1.17549435e-38f, -1.17549435e-38f,
        1.0e-20f, -1.0e-20f, 1.0e-5f, -1.0e-5f, 0.1f, -0.1f, 0.25f, -0.25f,
        0.5f, -0.5f, 0.7853982f, -0.7853982f, 1.0f, -1.0f, 1.2f, -1.2f,
        2.0f, -2.0f, 2.5f, -2.5f, 3.0f, -3.0f, 3.14f, -3.14f,
    ];

    private static readonly float[] s_expDomainSingleBoundaries =
    [
        0.0f, -0.0f, float.Epsilon, -float.Epsilon, 0.5f, -0.5f, 1.0f, -1.0f,
        2.0f, -2.0f, 10.0f, -10.0f, 50.0f, -50.0f, 88.0f, 88.722839f,
        -87.0f, -87.3365f, -95.0f /* subnormal output */, -102.0f, -103.2789f, -104.0f,
    ];

    private static readonly float[] s_logDomainSingleBoundaries =
    [
        float.Epsilon, 2.0f * float.Epsilon, BitConverter.Int32BitsToSingle(0x007F_FFFF),
        1.17549435e-38f, 1.0e-20f, 1.0e-5f, 0.1f, 0.5f, 0.75f, 1.0f, 1.5f, 2.0f,
        MathF.E, 10.0f, 100.0f, 1.0e20f, float.MaxValue,
    ];

    private static readonly double[] s_trigDomainDoubleBoundaries =
    [
        0.0, -0.0, double.Epsilon, -double.Epsilon, 2.2250738585072014e-308, -2.2250738585072014e-308,
        1.0e-100, -1.0e-100, 1.0e-10, -1.0e-10, 0.1, -0.1, 0.25, -0.25,
        0.5, -0.5, 0.7853981633974483, -0.7853981633974483, 1.0, -1.0,
        2.0, -2.0, 2.5, -2.5, 3.0, -3.0, 3.14, -3.14,
    ];

    private static readonly double[] s_expDomainDoubleBoundaries =
    [
        0.0, -0.0, double.Epsilon, -double.Epsilon, 0.5, -0.5, 1.0, -1.0,
        2.0, -2.0, 10.0, -10.0, 100.0, -100.0, 709.0, 709.782712893384,
        -708.0, -708.3964185322641, -720.0 /* double subnormal output */, -744.0, -745.0,
    ];

    private static readonly double[] s_logDomainDoubleBoundaries =
    [
        double.Epsilon, 2.0 * double.Epsilon, BitConverter.Int64BitsToDouble(0x000F_FFFF_FFFF_FFFFL),
        2.2250738585072014e-308, 1.0e-200, 1.0e-20, 0.1, 0.5, 0.75, 1.0, 1.5, 2.0,
        Math.E, 10.0, 100.0, 1.0e200, double.MaxValue,
    ];

    private static readonly (string Column, OutputPolicy Policy)[] s_singleTranscendentalColumnPolicies =
    [
        ("MathF.Sin(x)", OutputPolicy.Ulp(3, "CUDA Programming Guide sinf <= 2 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)")),
        ("MathF.Cos(x)", OutputPolicy.Ulp(3, "CUDA Programming Guide cosf <= 2 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)")),
        ("MathF.Tan(x)", OutputPolicy.Ulp(5, "CUDA Programming Guide tanf <= 4 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)")),
        ("MathF.Exp(x)", OutputPolicy.Ulp(3, "CUDA Programming Guide expf <= 2 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)", allowInfinityBoundary: true)),
        ("MathF.Log(x)", OutputPolicy.Ulp(2, "CUDA Programming Guide logf <= 1 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)")),
        ("MathF.Pow(x,y)", OutputPolicy.Ulp(5, "CUDA Programming Guide powf <= 4 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)")),
    ];

    private static readonly (string Column, OutputPolicy Policy)[] s_doubleAndPromotedColumnPolicies =
    [
        ("Math.Sin(d)", OutputPolicy.Ulp(3, "CUDA Programming Guide sin <= 2 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)")),
        ("Math.Cos(d)", OutputPolicy.Ulp(3, "CUDA Programming Guide cos <= 2 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)")),
        ("Math.Tan(d)", OutputPolicy.Ulp(3, "CUDA Programming Guide tan <= 2 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)")),
        ("Math.Exp(d)", OutputPolicy.Ulp(2, "CUDA Programming Guide exp <= 1 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)", allowInfinityBoundary: true)),
        ("Math.Log(d)", OutputPolicy.Ulp(2, "CUDA Programming Guide log <= 1 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)")),
        ("Math.Pow(d1,d2)", OutputPolicy.Ulp(4, "CUDA Programming Guide pow <= 2 ulp + 2 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)")),
        ("Math.Sin(f)", OutputPolicy.Ulp(3, "CUDA Programming Guide sin(double) <= 2 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8) (#876)")),
        ("Math.Cos(f)", OutputPolicy.Ulp(3, "CUDA Programming Guide cos(double) <= 2 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8) (#876)")),
        ("Math.Tan(f)", OutputPolicy.Ulp(3, "CUDA Programming Guide tan(double) <= 2 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8) (#876)")),
        ("Math.Exp(f)", OutputPolicy.Ulp(2, "CUDA Programming Guide exp(double) <= 1 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8) (#876)", allowInfinityBoundary: true)),
        ("Math.Log(|f|+0.25f)", OutputPolicy.Ulp(2, "CUDA Programming Guide log(double) <= 1 ulp + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8) (#876)")),
        ("Math.Pow(|f|+0.25f,fExp)", OutputPolicy.Ulp(4, "CUDA Programming Guide pow(double) <= 2 ulp + 2 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8) (#876)")),
    ];

    private static readonly (string Column, OutputPolicy Policy)[] s_fastMathColumnPolicies =
    [
        ("FastMath.Sqrt(|x|)", OutputPolicy.Bitwise),
        ("FastMath.Abs(x)", OutputPolicy.Bitwise),
        ("FastMath.Exp(expX)", OutputPolicy.Ulp(14, "CUDA Programming Guide __expf <= 2 + floor(|1.173*x|) ulp on [-10,10] + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)")),
        ("FastMath.Log(logUnitX)", OutputPolicy.Ignore),
        ("FastMath.Log(logWideX)", OutputPolicy.Ulp(4, "CUDA Programming Guide __logf <= 3 ulp outside [0.5,2.0] + 1 ulp margin for the host result (measured CUDA vs .NET: at most 1 ulp, seeds 0-8)")),
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
        Oracle = static (b, _) => MathDifferentialKernels.MathOfSingleMatrixKernel(b.Get<float>("f"), b.Get<double>("r")),
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
        Oracle = static (b, _) => MathDifferentialKernels.SingleRoundingAndUnaryMatrixKernel(b.Get<float>("f"), b.Get<float>("r")),
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
        Oracle = static (b, _) => MathDifferentialKernels.DoubleRoundingAndUnaryMatrixKernel(b.Get<double>("d"), b.Get<double>("r")),
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
        Oracle = static (b, _) => MathDifferentialKernels.SingleMaxMinClampMatrixKernel(
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
        Oracle = static (b, _) => MathDifferentialKernels.DoubleMaxMinClampMatrixKernel(
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
        Oracle = static (b, _) => MathDifferentialKernels.MixedFloatMaxMinMatrixKernel(
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
        Oracle = static (b, _) => MathDifferentialKernels.Int32MathMatrixKernel(
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
        Oracle = static (b, _) => MathDifferentialKernels.MathFIntArgumentMatrixKernel(
            b.Get<int>("v"),
            b.Get<int>("min"),
            b.Get<int>("max"),
            b.Get<float>("rf")
        ),
    };

    // Case N01: Subnormal IEEE 754 0-ulp arithmetic and Sqrt without FTZ
    private static readonly DifferentialCase s_subnormalIeeeExact = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.SubnormalIeeeExactMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(DiffValues.SubnormalSingleBoundaries.Length * DiffValues.SubnormalSingleBoundaries.Length, blockX: 64),
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
        Oracle = static (b, _) => MathDifferentialKernels.SubnormalIeeeExactMatrixKernel(
            b.Get<float>("fa"),
            b.Get<float>("fb"),
            b.Get<double>("da"),
            b.Get<double>("db"),
            b.Get<float>("rf"),
            b.Get<double>("rd")
        ),
    };

    // Case N02: Transcendental and Pow special values / fixed points (Bitwise)
    private static readonly DifferentialCase s_transcendentalSpecialValues = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.TranscendentalSpecialValuesKernel)}")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) =>
        {
            var fx = DiffValues.Cycled(rng, rows, s_trigExpSpecialSingles, static r => s_trigExpSpecialSingles[r.NextBelow(s_trigExpSpecialSingles.Length)]);
            var logF = DiffValues.Cycled(rng, rows, s_logSpecialSingles, static r => s_logSpecialSingles[r.NextBelow(s_logSpecialSingles.Length)]);
            var powBaseF = new float[rows];
            var powExpF = new float[rows];
            var dx = DiffValues.Cycled(rng, rows, s_trigExpSpecialDoubles, static r => s_trigExpSpecialDoubles[r.NextBelow(s_trigExpSpecialDoubles.Length)]);
            var logD = DiffValues.Cycled(rng, rows, s_logSpecialDoubles, static r => s_logSpecialDoubles[r.NextBelow(s_logSpecialDoubles.Length)]);
            var powBaseD = new double[rows];
            var powExpD = new double[rows];

            for (int i = 0; i < rows; i++)
            {
                int pfIdx = rng.IsBoundarySeed || rng.NextBool()
                    ? i % s_powSpecialSinglePairs.Length
                    : rng.NextBelow(s_powSpecialSinglePairs.Length);
                powBaseF[i] = s_powSpecialSinglePairs[pfIdx].Base;
                powExpF[i] = s_powSpecialSinglePairs[pfIdx].Exp;

                int pdIdx = rng.IsBoundarySeed || rng.NextBool()
                    ? i % s_powSpecialDoublePairs.Length
                    : rng.NextBelow(s_powSpecialDoublePairs.Length);
                powBaseD[i] = s_powSpecialDoublePairs[pdIdx].Base;
                powExpD[i] = s_powSpecialDoublePairs[pdIdx].Exp;
            }

            return new DiffBuffers()
                .In("fx", fx)
                .In("logF", logF)
                .In("powBaseF", powBaseF)
                .In("powExpF", powExpF)
                .In("dx", dx)
                .In("logD", logD)
                .In("powBaseD", powBaseD)
                .In("powExpD", powExpD)
                .Out<float>("rf", rows, MathDifferentialKernels.TranscendentalSpecialSingleColumns, OutputPolicy.Bitwise)
                .Out<double>("rd", rows, MathDifferentialKernels.TranscendentalSpecialDoubleColumns, OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => MathDifferentialKernels.LaunchTranscendentalSpecialValuesKernel(
            b.Get<float>("fx"),
            b.Get<float>("logF"),
            b.Get<float>("powBaseF"),
            b.Get<float>("powExpF"),
            b.Get<double>("dx"),
            b.Get<double>("logD"),
            b.Get<double>("powBaseD"),
            b.Get<double>("powExpD"),
            b.Get<float>("rf"),
            b.Get<double>("rd"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => MathDifferentialKernels.TranscendentalSpecialValuesKernel(
            b.Get<float>("fx"),
            b.Get<float>("logF"),
            b.Get<float>("powBaseF"),
            b.Get<float>("powExpF"),
            b.Get<double>("dx"),
            b.Get<double>("logD"),
            b.Get<double>("powBaseD"),
            b.Get<double>("powExpD"),
            b.Get<float>("rf"),
            b.Get<double>("rd")
        ),
    };

    // Case N03: System.Numerics.BitOperations all 18 mapped overloads + shift count masking + named arguments
    private static readonly DifferentialCase s_bitOperations = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.BitOperationsMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(DiffValues.BitOperationUInt32Boundaries.Length * DiffValues.ShiftCounts.Length, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var u32 = new uint[rows];
            var u64 = new ulong[rows];
            var offsets = new int[rows];
            int nVal = DiffValues.BitOperationUInt32Boundaries.Length;
            int nShift = DiffValues.ShiftCounts.Length;

            for (int r = 0; r < rows; r++)
            {
                if (rng.IsBoundarySeed)
                {
                    u32[r] = DiffValues.BitOperationUInt32Boundaries[r / nShift % nVal];
                    u64[r] = DiffValues.BitOperationUInt64Boundaries[r / nShift % DiffValues.BitOperationUInt64Boundaries.Length];
                    offsets[r] = DiffValues.ShiftCounts[r % nShift];
                }
                else
                {
                    u32[r] = rng.NextBool()
                        ? DiffValues.BitOperationUInt32Boundaries[rng.NextBelow(nVal)]
                        : rng.NextUInt32();
                    u64[r] = rng.NextBool()
                        ? DiffValues.BitOperationUInt64Boundaries[rng.NextBelow(DiffValues.BitOperationUInt64Boundaries.Length)]
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
        Oracle = static (b, _) => MathDifferentialKernels.BitOperationsMatrixKernel(
            b.Get<uint>("u32"),
            b.Get<ulong>("u64"),
            b.Get<int>("offsets"),
            b.Get<ulong>("r")
        ),
    };

    // Case N05: IEEE 754 unordered comparisons and large-exponent Round ties
    private static readonly DifferentialCase s_floatComparisonAndLargeTie = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.FloatComparisonAndLargeTieMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(s_singleRoundBoundaries.Length * 8, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var (fa, fb) = DiffValues.Pairs(rng, rows, s_singleRoundBoundaries, DiffValues.NextSingle);
            var (da, db) = DiffValues.Pairs(rng, rows, s_doubleBoundaries, NextDouble);
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
        Oracle = static (b, _) => MathDifferentialKernels.FloatComparisonAndLargeTieMatrixKernel(
            b.Get<float>("fa"),
            b.Get<float>("fb"),
            b.Get<double>("da"),
            b.Get<double>("db"),
            b.Get<int>("ia"),
            b.Get<byte>("rCmp"),
            b.Get<double>("rRound")
        ),
    };

    // Case N06: Mul-Add exact inputs (Bitwise) and FMA contraction witness (Verify)
    private static readonly DifferentialCase s_mulAddExactAndContraction = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.MulAddExactAndContractionKernel)}")
    {
        Shape = LaunchShape.ForRows(128, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var fa = new float[rows];
            var fb = new float[rows];
            var fc = new float[rows];

            for (int i = 0; i < rows; i++)
            {
                // fa has <= 12 mantissa bits and fb has <= 11 mantissa bits so fa * fb is exact in binary32 (no intermediate rounding)
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
        Oracle = static (b, _) => MathDifferentialKernels.MulAddExactAndContractionKernel(
            b.Get<float>("fa"),
            b.Get<float>("fb"),
            b.Get<float>("fc"),
            b.Get<float>("rExact"),
            b.Get<float>("rContract")
        ),
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

                bool matchesFma = BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(fmaExpected);
                bool matchesTwoStep = BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(twoStepExpected);
                if (!matchesFma && !matchesTwoStep)
                {
                    return PropertyResult.Fail(
                        $"row {i} (k={k}): rContract={actual} matches neither FMA ({fmaExpected}) nor two-step ({twoStepExpected})"
                    );
                }

#if GSHARP_CUDA
                if (i == 0 && !matchesFma)
                {
                    return PropertyResult.Fail(
                        $"CUDA row 0 (k={k}): expected FMA contracted result {fmaExpected} (2^-24), got {actual}"
                    );
                }
#endif
            }

            return PropertyResult.Pass();
        },
    };

    // Case N07: MathF single-precision transcendentals (ULP + Verify)
    private static readonly DifferentialCase s_singleTranscendental = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.SingleTranscendentalMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(256, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var trigX = DiffValues.Mixed(rng, rows, s_trigDomainSingleBoundaries, NextSafeTrigSingle);
            var expX = DiffValues.Mixed(rng, rows, s_expDomainSingleBoundaries, static r => -104.0f + (float)(r.NextUInt32() / (double)uint.MaxValue) * 192.5f);
            var logX = DiffValues.Mixed(rng, rows, s_logDomainSingleBoundaries, NextPositiveSingle);
            var powBase = new float[rows];
            var powExp = new float[rows];

            for (int i = 0; i < rows; i++)
            {
                powBase[i] = 0.1f + (float)((rng.IsBoundarySeed ? (i % 64) / 64.0 : rng.NextUInt32() / (double)uint.MaxValue) * 9.9);
                powExp[i] = -5.0f + (float)((rng.IsBoundarySeed ? ((i * 7) % 64) / 64.0 : rng.NextUInt32() / (double)uint.MaxValue) * 10.0);
            }

            if (rows > 2)
            {
                expX[0] = -95.0f;
                logX[0] = float.Epsilon;
                trigX[0] = 1.0e-20f;
            }

            return new DiffBuffers()
                .In("trigX", trigX)
                .In("expX", expX)
                .In("logX", logX)
                .In("powBase", powBase)
                .In("powExp", powExp)
                .Out<float>("r", rows, s_singleTranscendentalColumnPolicies);
        },
        Launch = static (b, s) => MathDifferentialKernels.LaunchSingleTranscendentalMatrixKernel(
            b.Get<float>("trigX"),
            b.Get<float>("expX"),
            b.Get<float>("logX"),
            b.Get<float>("powBase"),
            b.Get<float>("powExp"),
            b.Get<float>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => MathDifferentialKernels.SingleTranscendentalMatrixKernel(
            b.Get<float>("trigX"),
            b.Get<float>("expX"),
            b.Get<float>("logX"),
            b.Get<float>("powBase"),
            b.Get<float>("powExp"),
            b.Get<float>("r")
        ),
        Verify = static (b, s) =>
        {
            var logX = b.Get<float>("logX");
            var r = b.Get<float>("r");
            for (int i = 0; i < s.Rows; i++)
            {
                int o = i * 6;
                float sin = r[o];
                float cos = r[o + 1];
                float logVal = r[o + 4];
                if (MathF.Abs(sin) > 1.000001f || MathF.Abs(cos) > 1.000001f)
                {
                    return PropertyResult.Fail($"row {i}: |sin|={MathF.Abs(sin)} or |cos|={MathF.Abs(cos)} exceeds 1");
                }

                float pythagoras = MathF.Abs(sin * sin + cos * cos - 1.0f);
                if (pythagoras > 8.0f * 1.1920929e-7f)
                {
                    return PropertyResult.Fail($"row {i}: |sin^2 + cos^2 - 1| = {pythagoras} exceeds bound");
                }

                if (logX[i] > 0.0f && logX[i] < 1.17549435e-38f && (!float.IsFinite(logVal) || logVal >= -87.3f))
                {
                    return PropertyResult.Fail($"row {i}: subnormal log({logX[i]}) produced {logVal}");
                }
            }

            return PropertyResult.Pass();
        },
    };

    // Case N08: System.Math double-precision transcendentals + float argument widening (#876)
    private static readonly DifferentialCase s_doubleAndPromotedSingleTranscendental = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.DoubleAndPromotedSingleTranscendentalMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(256, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var trigD = DiffValues.Mixed(rng, rows, s_trigDomainDoubleBoundaries, NextSafeTrigDouble);
            var expD = DiffValues.Mixed(rng, rows, s_expDomainDoubleBoundaries, static r => -744.0 + (r.NextUInt64() / (double)ulong.MaxValue) * 1453.0);
            var logD = DiffValues.Mixed(rng, rows, s_logDomainDoubleBoundaries, NextPositiveDouble);
            var powBaseD = new double[rows];
            var powExpD = new double[rows];
            var fArg = DiffValues.Mixed(rng, rows, s_trigDomainSingleBoundaries, NextSafeTrigSingle);
            var fExp = new float[rows];

            for (int i = 0; i < rows; i++)
            {
                powBaseD[i] = 0.1 + ((rng.IsBoundarySeed ? (i % 64) / 64.0 : rng.NextUInt64() / (double)ulong.MaxValue) * 9.9);
                powExpD[i] = -5.0 + ((rng.IsBoundarySeed ? ((i * 7) % 64) / 64.0 : rng.NextUInt64() / (double)ulong.MaxValue) * 10.0);
                fExp[i] = -4.0f + (float)((rng.IsBoundarySeed ? ((i * 5) % 64) / 64.0 : rng.NextUInt32() / (double)uint.MaxValue) * 8.0);
            }

            if (rows > 0)
            {
                expD[0] = -720.0;
                logD[0] = double.Epsilon;
                fArg[0] = 0.5f;
                fExp[0] = 1.5f;
            }

            return new DiffBuffers()
                .In("trigD", trigD)
                .In("expD", expD)
                .In("logD", logD)
                .In("powBaseD", powBaseD)
                .In("powExpD", powExpD)
                .In("fArg", fArg)
                .In("fExp", fExp)
                .Out<double>("r", rows, s_doubleAndPromotedColumnPolicies);
        },
        Launch = static (b, s) => MathDifferentialKernels.LaunchDoubleAndPromotedSingleTranscendentalMatrixKernel(
            b.Get<double>("trigD"),
            b.Get<double>("expD"),
            b.Get<double>("logD"),
            b.Get<double>("powBaseD"),
            b.Get<double>("powExpD"),
            b.Get<float>("fArg"),
            b.Get<float>("fExp"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => MathDifferentialKernels.DoubleAndPromotedSingleTranscendentalMatrixKernel(
            b.Get<double>("trigD"),
            b.Get<double>("expD"),
            b.Get<double>("logD"),
            b.Get<double>("powBaseD"),
            b.Get<double>("powExpD"),
            b.Get<float>("fArg"),
            b.Get<float>("fExp"),
            b.Get<double>("r")
        ),
    };

    // Case N09: FastMath (Sqrt, Abs, Exp, Log)
    private static readonly DifferentialCase s_fastMath = new($"{nameof(MathDifferentialKernels)}.{nameof(MathDifferentialKernels.FastMathMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(128, blockX: 64),
        Inputs = static (rng, rows) =>
        {
            var x = DiffValues.Mixed(rng, rows, DiffValues.SubnormalSingleBoundaries, DiffValues.NextSingle);
            var expX = new float[rows];
            var logUnitX = new float[rows];
            var logWideX = new float[rows];

            for (int i = 0; i < rows; i++)
            {
                double u1 = rng.IsBoundarySeed ? (i % 64) / 63.0 : rng.NextUInt32() / (double)uint.MaxValue;
                double u2 = rng.IsBoundarySeed ? ((i * 5) % 64) / 63.0 : rng.NextUInt32() / (double)uint.MaxValue;
                expX[i] = (float)(-10.0 + u1 * 20.0);
                logUnitX[i] = (float)(0.5 + u2 * 1.5);
                int expIdx = rng.IsBoundarySeed ? (i % 38) : rng.NextBelow(38);
                int expPow = expIdx < 19 ? (expIdx - 21) : (expIdx - 17); // [-21..-3] U [2..20], strictly outside [0.5, 2.0]
                logWideX[i] = MathF.ScaleB((float)(1.0 + u1 * 0.999), expPow);
            }

            if (rows > 0)
            {
                expX[0] = 10.0f;
                logUnitX[0] = 0.75f;
                logWideX[0] = 1.0e6f;
            }

            return new DiffBuffers()
                .In("x", x)
                .In("expX", expX)
                .In("logUnitX", logUnitX)
                .In("logWideX", logWideX)
                .Out<float>("r", rows, s_fastMathColumnPolicies);
        },
        Launch = static (b, s) => MathDifferentialKernels.LaunchFastMathMatrixKernel(
            b.Get<float>("x"),
            b.Get<float>("expX"),
            b.Get<float>("logUnitX"),
            b.Get<float>("logWideX"),
            b.Get<float>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => MathDifferentialKernels.FastMathMatrixKernel(
            b.Get<float>("x"),
            b.Get<float>("expX"),
            b.Get<float>("logUnitX"),
            b.Get<float>("logWideX"),
            b.Get<float>("r")
        ),
        Verify = static (b, s) =>
        {
            var logUnitX = b.Get<float>("logUnitX");
            var r = b.Get<float>("r");
            const float maxAbsError = 4.8e-7f; // CUDA Programming Guide __logf abs error <= 2^-21.41 (~3.576e-7) on [0.5, 2.0] + host 1 ulp
            for (int i = 0; i < s.Rows; i++)
            {
                float actual = r[i * 5 + 3];
                float expected = MathF.Log(logUnitX[i]);
                float diff = MathF.Abs(actual - expected);
                if (diff > maxAbsError)
                {
                    return PropertyResult.Fail($"row {i} (x={logUnitX[i]}): FastMath.Log={actual}, expected={expected}, diff={diff}");
                }
            }

            return PropertyResult.Pass();
        },
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

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SubnormalIeeeExact_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_subnormalIeeeExact);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_TranscendentalSpecialValues_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_transcendentalSpecialValues);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_BitOperations_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_bitOperations);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_FloatComparisonAndLargeTie_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_floatComparisonAndLargeTie);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_MulAddExactAndContraction_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_mulAddExactAndContraction);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_SingleTranscendental_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_singleTranscendental);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DoubleAndPromotedSingleTranscendental_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_doubleAndPromotedSingleTranscendental);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_FastMath_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_fastMath);

    // BclMappings maps all 6 BitOperations nuint overloads, but CppTypeNameWriter emits
    // undeclared System_UIntPtr for nuint locals/casts so AOT C++/CUDA compilation would fail with C2065 (#983)
    [Fact(Skip = "Blocked by #983 (nint and nuint constants and casts in kernels build wrong or not at all)")]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_BitOperationsNuintOverloads_EmitIntrinsicCallsWithoutUndeclaredSystemUIntPtr()
    {
        var source = Helpers.RoslynTestUtility.ReadTestCase("Differential/BitOpsNuintProbeKernels.cs");
        var (driver, _, _) = Helpers.RoslynTestUtility.RunGenerator(source, "Cpu");
        var sb = new System.Text.StringBuilder();
        foreach (var tree in driver.GetRunResult().GeneratedTrees)
        {
            sb.Append(tree.GetText());
        }

        var generated = sb.ToString();

        Assert.DoesNotContain("System_UIntPtr", generated, StringComparison.Ordinal);
        Assert.Contains("__gsharp_lzc_64((unsigned long long int)(", generated, StringComparison.Ordinal);
        Assert.Contains("__gsharp_tzc_64((unsigned long long int)(", generated, StringComparison.Ordinal);
        Assert.Contains("__popcll((unsigned long long int)(", generated, StringComparison.Ordinal);
        Assert.Contains("__gsharp_log2_64((unsigned long long int)(", generated, StringComparison.Ordinal);
        Assert.Contains("__gsharp_rotl64((unsigned long long int)(", generated, StringComparison.Ordinal);
        Assert.Contains("__gsharp_rotr64((unsigned long long int)(", generated, StringComparison.Ordinal);
    }

    private static float[] SingleRoundValues(DiffRandom rng, int count) =>
        DiffValues.Mixed(rng, count, s_singleRoundBoundaries, DiffValues.NextSingle);

    private static double[] DoubleValues(DiffRandom rng, int count) =>
        DiffValues.Values(rng, count, s_doubleBoundaries, NextDouble);

    private static float NextSubnormalSingle(DiffRandom rng)
    {
        if (rng.NextBool())
        {
            return DiffValues.SubnormalSingleBoundaries[rng.NextBelow(DiffValues.SubnormalSingleBoundaries.Length)];
        }

        uint sign = rng.NextBool() ? 0x8000_0000u : 0u;
        uint mantissa = rng.NextUInt32() & 0x007F_FFFFu;
        return BitConverter.UInt32BitsToSingle(sign | mantissa);
    }

    private static double NextSubnormalDouble(DiffRandom rng)
    {
        if (rng.NextBool())
        {
            return DiffValues.SubnormalDoubleBoundaries[rng.NextBelow(DiffValues.SubnormalDoubleBoundaries.Length)];
        }

        ulong sign = rng.NextBool() ? 0x8000_0000_0000_0000UL : 0UL;
        ulong mantissa = rng.NextUInt64() & 0x000F_FFFF_FFFF_FFFFUL;
        return BitConverter.UInt64BitsToDouble(sign | mantissa);
    }

    private static float NextSafeTrigSingle(DiffRandom rng)
    {
        double u = rng.NextUInt32() / (double)uint.MaxValue;
        float x = (float)((u * 2.0 - 1.0) * 3.14);
        if (MathF.Abs(MathF.Abs(x) - MathF.PI * 0.5f) < 0.02f)
        {
            x += 0.05f;
        }

        return x;
    }

    private static double NextSafeTrigDouble(DiffRandom rng)
    {
        double u = rng.NextUInt64() / (double)ulong.MaxValue;
        double x = (u * 2.0 - 1.0) * 3.14;
        if (Math.Abs(Math.Abs(x) - Math.PI * 0.5) < 0.02)
        {
            x += 0.05;
        }

        return x;
    }

    private static float NextPositiveSingle(DiffRandom rng)
    {
        uint exp = (uint)rng.NextBelow(254); // 0 (subnormal) .. 253 (normal)
        uint mant = rng.NextUInt32() & 0x007F_FFFFu;
        if (exp == 0 && mant == 0)
        {
            mant = 1;
        }

        return BitConverter.UInt32BitsToSingle((exp << 23) | mant);
    }

    private static double NextPositiveDouble(DiffRandom rng)
    {
        ulong exp = (ulong)rng.NextBelow(2046); // 0 (subnormal) .. 2045 (normal)
        ulong mant = rng.NextUInt64() & 0x000F_FFFF_FFFF_FFFFUL;
        if (exp == 0 && mant == 0)
        {
            mant = 1;
        }

        return BitConverter.UInt64BitsToDouble((exp << 52) | mant);
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
