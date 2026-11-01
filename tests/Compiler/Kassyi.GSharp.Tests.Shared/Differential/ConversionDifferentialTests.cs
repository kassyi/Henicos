#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for conversions (<see cref="ConversionDifferentialKernels"/>): <c>foreach</c> element conversions,
/// enum casts, 32-bit/64-bit integer-to-float/double rounding boundaries, and scalar <c>char</c> kernel arguments.
/// </summary>
public class ConversionDifferentialTests
{
    private static readonly double[] s_doubleBoundaries =
    [
        0.0,
        -0.0,
        1.0,
        -1.0,
        0.5,
        -0.5,
        1.5,
        -1.5,
        255.9,
        256.0,
        -128.9,
        -129.0,
        65535.9,
        65536.0,
        2147483647.0,
        2147483648.0,
        -2147483648.0,
        -2147483649.0,
        4294967295.0,
        4294967296.0,
        9.223372036854776e18,
        -9.223372036854776e18,
        1.8446744073709552e19,
        1e20,
        -1e20,
        double.Epsilon,
        -double.Epsilon,
        double.MaxValue,
        double.MinValue,
        double.PositiveInfinity,
        double.NegativeInfinity,
        double.NaN,
        BitConverter.UInt64BitsToDouble(0xFFF8_0000_0000_0001UL),
    ];

    // 64-bit unsigned boundaries, including IEEE-754 float (24-bit significand) and double (53-bit significand)
    // halfway rounding ties and double-rounding witnesses (where 64->53->24 bit rounding differs from 64->24 bit)
    private static readonly ulong[] s_uint64Boundaries =
    [
        0UL,
        1UL,
        2UL,
        255UL,
        65535UL,
        (1UL << 24) - 1,
        1UL << 24,
        (1UL << 24) + 1,
        (1UL << 24) + 2,
        (1UL << 24) + 3,
        (1UL << 25) + 1,
        (1UL << 25) + 3,
        uint.MaxValue,
        (1UL << 32) + 1,
        (1UL << 53) - 1,
        1UL << 53,
        (1UL << 53) + 1,
        (1UL << 53) + 2,
        (1UL << 53) + 3,
        (1UL << 54) + 1,
        (1UL << 54) + 3,
        0x8000_007F_FFFF_FC00UL,
        0x8000_007F_FFFF_FC01UL,
        0x8000_0080_0000_0000UL,
        0x8000_0080_0000_0001UL,
        0x8000_017F_FFFF_FC00UL,
        0x8000_017F_FFFF_FC01UL,
        0x8000_0180_0000_0000UL,
        0x8000_0180_0000_0001UL,
        0xFFFF_FF80_0000_0000UL,
        0xFFFF_FF80_0000_0001UL,
        (ulong)long.MaxValue,
        (ulong)long.MaxValue + 1UL,
        ulong.MaxValue - 1UL,
        ulong.MaxValue,
    ];

    private static readonly long[] s_int64Boundaries =
    [
        0L,
        1L,
        -1L,
        2L,
        -2L,
        (1L << 24) - 1,
        1L << 24,
        (1L << 24) + 1,
        -((1L << 24) + 1),
        (1L << 25) + 1,
        -((1L << 25) + 1),
        int.MaxValue,
        int.MinValue,
        (1L << 53) - 1,
        1L << 53,
        (1L << 53) + 1,
        -((1L << 53) + 1),
        (1L << 53) + 3,
        -((1L << 53) + 3),
        0x4000_003F_FFFF_FE01L,
        -0x4000_003F_FFFF_FE01L,
        0x4000_0040_0000_0001L,
        -0x4000_0040_0000_0001L,
        0x4000_00BF_FFFF_FE01L,
        -0x4000_00BF_FFFF_FE01L,
        0x7FFF_FFBF_FFFF_FFFFL,
        0x7FFF_FFC0_0000_0000L,
        0x7FFF_FFC0_0000_0001L,
        -0x7FFF_FFC0_0000_0001L,
        long.MaxValue - 1L,
        long.MaxValue,
        long.MinValue + 1L,
        long.MinValue,
    ];

    private static readonly char[] s_charBoundaries =
    [
        '\u0020',
        '\u0001',
        'A',
        '\u007F',
        '\u0080',
        '\u00FF',
        '\u0100',
        '\u1234',
        '\u7FFF',
        '\u8000',
        '\uFFFE',
        '\uFFFF',
    ];

    private static readonly DifferentialCase s_foreachFloatToInt = new($"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.ForeachFloatToIntMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("f", DiffValues.Single(rng, rows))
            .In("d", DoubleValues(rng, rows))
            .Out<long>("r", rows, ConversionDifferentialKernels.ForeachFloatToIntColumns, OutputPolicy.Exact),
        Launch = static (b, s) => ConversionDifferentialKernels.LaunchForeachFloatToIntMatrixKernel(
            b.Get<float>("f"),
            b.Get<double>("d"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => ConversionDifferentialKernels.ForeachFloatToIntMatrixKernel(
            b.Get<float>("f"),
            b.Get<double>("d"),
            b.Get<long>("r")
        ),
    };

    private static readonly DifferentialCase s_enumCast = new($"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.EnumCastMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(64, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("f", DiffValues.Single(rng, rows))
            .In("d", DoubleValues(rng, rows))
            .In("i", DiffValues.Int32(rng, rows))
            .Out<long>("r", rows, ConversionDifferentialKernels.EnumCastColumns, OutputPolicy.Exact),
        Launch = static (b, s) => ConversionDifferentialKernels.LaunchEnumCastMatrixKernel(
            b.Get<float>("f"),
            b.Get<double>("d"),
            b.Get<int>("i"),
            b.Get<long>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => ConversionDifferentialKernels.EnumCastMatrixKernel(
            b.Get<float>("f"),
            b.Get<double>("d"),
            b.Get<int>("i"),
            b.Get<long>("r")
        ),
    };

    private static readonly DifferentialCase s_intToFloat = new($"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.IntToFloatConversionMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(70, blockX: 32),
        Inputs = static (rng, rows) =>
        {
            var i = DiffValues.Int32(rng, rows);
            var u = Array.ConvertAll(DiffValues.Int32(rng, rows), static v => unchecked((uint)v));
            return new DiffBuffers()
                .In("i", i)
                .In("u", u)
                .In("l", Int64Values(rng, rows))
                .In("ul", UInt64Values(rng, rows))
                .Out<float>("r", rows, ConversionDifferentialKernels.IntToFloatColumns, OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => ConversionDifferentialKernels.LaunchIntToFloatConversionMatrixKernel(
            b.Get<int>("i"),
            b.Get<uint>("u"),
            b.Get<long>("l"),
            b.Get<ulong>("ul"),
            b.Get<float>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => ConversionDifferentialKernels.IntToFloatConversionMatrixKernel(
            b.Get<int>("i"),
            b.Get<uint>("u"),
            b.Get<long>("l"),
            b.Get<ulong>("ul"),
            b.Get<float>("r")
        ),
    };

    private static readonly DifferentialCase s_intToDouble = new($"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.IntToDoubleConversionMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(70, blockX: 32),
        Inputs = static (rng, rows) =>
        {
            var i = DiffValues.Int32(rng, rows);
            var u = Array.ConvertAll(DiffValues.Int32(rng, rows), static v => unchecked((uint)v));
            return new DiffBuffers()
                .In("i", i)
                .In("u", u)
                .In("l", Int64Values(rng, rows))
                .In("ul", UInt64Values(rng, rows))
                .Out<double>("r", rows, ConversionDifferentialKernels.IntToDoubleColumns, OutputPolicy.Bitwise);
        },
        Launch = static (b, s) => ConversionDifferentialKernels.LaunchIntToDoubleConversionMatrixKernel(
            b.Get<int>("i"),
            b.Get<uint>("u"),
            b.Get<long>("l"),
            b.Get<ulong>("ul"),
            b.Get<double>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => ConversionDifferentialKernels.IntToDoubleConversionMatrixKernel(
            b.Get<int>("i"),
            b.Get<uint>("u"),
            b.Get<long>("l"),
            b.Get<ulong>("ul"),
            b.Get<double>("r")
        ),
    };

    private static readonly DifferentialCase s_charScalarArg = new($"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.CharScalarArgKernel)}")
    {
        Shape = LaunchShape.ForRows(24, blockX: 16),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("cArr", CharValues(rng, rows))
            .Out<int>("r", rows, ConversionDifferentialKernels.CharScalarArgColumns, OutputPolicy.Exact),
        Launch = static (b, s) => ConversionDifferentialKernels.LaunchCharScalarArgKernel(
            '\u1234',
            b.Get<char>("cArr"),
            b.Get<int>("r"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static b => ConversionDifferentialKernels.CharScalarArgKernel(
            '\u1234',
            b.Get<char>("cArr"),
            b.Get<int>("r")
        ),
    };

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_ForeachFloatToInt_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_foreachFloatToInt);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_EnumCast_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_enumCast);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_IntToFloatConversion_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_intToFloat);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_IntToDoubleConversion_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_intToDouble);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_CharScalarArg_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_charScalarArg);

    private static double[] DoubleValues(DiffRandom rng, int count) =>
        DiffValues.Mixed(rng, count, s_doubleBoundaries, static r => BitConverter.UInt64BitsToDouble(r.NextUInt64()));

    private static ulong[] UInt64Values(DiffRandom rng, int count) =>
        DiffValues.Mixed(rng, count, s_uint64Boundaries, static r => r.NextUInt64());

    private static long[] Int64Values(DiffRandom rng, int count) =>
        DiffValues.Mixed(rng, count, s_int64Boundaries, static r => unchecked((long)r.NextUInt64()));

    private static char[] CharValues(DiffRandom rng, int count) =>
        DiffValues.Mixed(rng, count, s_charBoundaries, static r => (char)r.NextUInt64());
}
