#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for conversions (<see cref="ConversionDifferentialKernels"/>): <c>foreach</c> element conversions,
/// enum casts, 32-bit/64-bit integer-to-float/double rounding boundaries, scalar <c>char</c> kernel arguments,
/// direct float/double casts to all 9 integer types, and integer widening/narrowing casts and shift masks.
/// </summary>
public class ConversionDifferentialTests
{
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
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
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
        Oracle = static (b, _) => ConversionDifferentialKernels.ForeachFloatToIntMatrixKernel(
            b.Get<float>("f"),
            b.Get<double>("d"),
            b.Get<long>("r")
        ),
    };

    private static readonly DifferentialCase s_enumCast = new($"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.EnumCastMatrixKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(64),
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
        Oracle = static (b, _) => ConversionDifferentialKernels.EnumCastMatrixKernel(
            b.Get<float>("f"),
            b.Get<double>("d"),
            b.Get<int>("i"),
            b.Get<long>("r")
        ),
    };

    private static readonly DifferentialCase s_intToFloat = new($"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.IntToFloatConversionMatrixKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(70),
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
        Oracle = static (b, _) => ConversionDifferentialKernels.IntToFloatConversionMatrixKernel(
            b.Get<int>("i"),
            b.Get<uint>("u"),
            b.Get<long>("l"),
            b.Get<ulong>("ul"),
            b.Get<float>("r")
        ),
    };

    private static readonly DifferentialCase s_intToDouble = new($"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.IntToDoubleConversionMatrixKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(70),
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
        Oracle = static (b, _) => ConversionDifferentialKernels.IntToDoubleConversionMatrixKernel(
            b.Get<int>("i"),
            b.Get<uint>("u"),
            b.Get<long>("l"),
            b.Get<ulong>("ul"),
            b.Get<double>("r")
        ),
    };

    private static readonly DifferentialCase s_charScalarArg = new($"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.CharScalarArgKernel)}")
    {
        Shapes = LaunchShapes.OneDimensionalMatrix(24),
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
        Oracle = static (b, _) => ConversionDifferentialKernels.CharScalarArgKernel(
            '\u1234',
            b.Get<char>("cArr"),
            b.Get<int>("r")
        ),
    };

    // Direct casts between float/double and all 9 integer types (#724, #890)
    private static readonly DifferentialCase s_directFloatDoubleCast = new($"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.DirectFloatDoubleCastMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(72, blockX: 32),
        Inputs = static (rng, rows) => new DiffBuffers()
            .In("f", DiffValues.Mixed(rng, rows, DiffValues.ConversionEdgeSingles, static r => BitConverter.UInt32BitsToSingle((uint)r.NextUInt64())))
            .In("d", DoubleValues(rng, rows))
            .In("i64", Int64Values(rng, rows))
            .In("u64", UInt64Values(rng, rows))
            .Out<long>("rSigned", rows, ConversionDifferentialKernels.DirectSignedCastColumns, OutputPolicy.Exact)
            .Out<ulong>("rUnsigned", rows, ConversionDifferentialKernels.DirectUnsignedCastColumns, OutputPolicy.Exact)
            .Out<float>("rFloat", rows, ConversionDifferentialKernels.DirectFloatCastColumns, OutputPolicy.Bitwise)
            .Out<double>("rDouble", rows, ConversionDifferentialKernels.DirectDoubleCastColumns, OutputPolicy.Bitwise),
        Launch = static (b, s) => ConversionDifferentialKernels.LaunchDirectFloatDoubleCastMatrixKernel(
            b.Get<float>("f"),
            b.Get<double>("d"),
            b.Get<long>("i64"),
            b.Get<ulong>("u64"),
            b.Get<long>("rSigned"),
            b.Get<ulong>("rUnsigned"),
            b.Get<float>("rFloat"),
            b.Get<double>("rDouble"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => ConversionDifferentialKernels.DirectFloatDoubleCastMatrixKernel(
            b.Get<float>("f"),
            b.Get<double>("d"),
            b.Get<long>("i64"),
            b.Get<ulong>("u64"),
            b.Get<long>("rSigned"),
            b.Get<ulong>("rUnsigned"),
            b.Get<float>("rFloat"),
            b.Get<double>("rDouble")
        ),
    };

    private static readonly DifferentialCase s_integerCastAndShiftMask = new($"{nameof(ConversionDifferentialKernels)}.{nameof(ConversionDifferentialKernels.IntegerCastAndShiftMaskMatrixKernel)}")
    {
        Shape = LaunchShape.ForRows(72, blockX: 32),
        Inputs = static (rng, rows) =>
        {
            var i32 = DiffValues.Int32(rng, rows);
            var u32 = DiffValues.Mixed(rng, rows, DiffValues.BitOperationUInt32Boundaries, static r => (uint)r.NextUInt64());
            var sh = DiffValues.Mixed(rng, rows, DiffValues.ShiftCounts, static r => unchecked((int)r.NextUInt64()));
            return new DiffBuffers()
                .In("i32", i32)
                .In("u32", u32)
                .In("i64", Int64Values(rng, rows))
                .In("u64", UInt64Values(rng, rows))
                .In("sh", sh)
                .Out<long>("rSigned", rows, ConversionDifferentialKernels.IntegerSignedCastShiftColumns, OutputPolicy.Exact)
                .Out<ulong>("rUnsigned", rows, ConversionDifferentialKernels.IntegerUnsignedCastShiftColumns, OutputPolicy.Exact);
        },
        Launch = static (b, s) => ConversionDifferentialKernels.LaunchIntegerCastAndShiftMaskMatrixKernel(
            b.Get<int>("i32"),
            b.Get<uint>("u32"),
            b.Get<long>("i64"),
            b.Get<ulong>("u64"),
            b.Get<int>("sh"),
            b.Get<long>("rSigned"),
            b.Get<ulong>("rUnsigned"),
            gridDimX: s.GridX,
            blockDimX: s.BlockX
        ),
        Oracle = static (b, _) => ConversionDifferentialKernels.IntegerCastAndShiftMaskMatrixKernel(
            b.Get<int>("i32"),
            b.Get<uint>("u32"),
            b.Get<long>("i64"),
            b.Get<ulong>("u64"),
            b.Get<int>("sh"),
            b.Get<long>("rSigned"),
            b.Get<ulong>("rUnsigned")
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

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DirectFloatDoubleCast_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_directFloatDoubleCast);

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_IntegerCastAndShiftMask_MatchesTheOracleOnEveryPath() => DifferentialRunner.Run(s_integerCastAndShiftMask);

    private static double[] DoubleValues(DiffRandom rng, int count) =>
        DiffValues.Mixed(rng, count, DiffValues.ConversionEdgeDoubles, static r => BitConverter.UInt64BitsToDouble(r.NextUInt64()));

    private static ulong[] UInt64Values(DiffRandom rng, int count) =>
        DiffValues.Mixed(rng, count, DiffValues.UInt64RoundingTieBoundaries, static r => r.NextUInt64());

    private static long[] Int64Values(DiffRandom rng, int count) =>
        DiffValues.Mixed(rng, count, DiffValues.Int64RoundingTieBoundaries, static r => unchecked((long)r.NextUInt64()));

    private static char[] CharValues(DiffRandom rng, int count) =>
        DiffValues.Mixed(rng, count, s_charBoundaries, static r => (char)r.NextUInt64());
}
