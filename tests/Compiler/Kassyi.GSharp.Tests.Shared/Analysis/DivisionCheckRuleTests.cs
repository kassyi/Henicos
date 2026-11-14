#nullable enable
using System.Numerics;
using Kassyi.GSharp.Compiler.Analysis.Checks;

namespace Kassyi.GSharp.Tests.Shared.Analysis;

/// <summary>
/// The table of <see cref="DivisionCheckRule.Decide"/> (#718 (Array and span bounds, integer division by zero,
/// Nullable.Value, Math.Clamp and Math.Abs(MinValue) are not checked as in C#)): per operator type and operand ranges,
/// whether the division needs its divisor check. The rule takes values only, so the cases need no compilation.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class DivisionCheckRuleTests
{
    private static readonly IntegerRange s_int32 = new(int.MinValue, int.MaxValue);
    private static readonly IntegerRange s_int64 = new(long.MinValue, long.MaxValue);
    private static readonly IntegerRange s_uint32 = new(uint.MinValue, uint.MaxValue);
    private static readonly IntegerRange s_uint64 = new(ulong.MinValue, ulong.MaxValue);
    private static readonly IntegerRange s_int16 = new(short.MinValue, short.MaxValue);
    private static readonly IntegerRange s_byte = new(byte.MinValue, byte.MaxValue);

    public static TheoryData<SpecialType, BigInteger, BigInteger, BigInteger, BigInteger> CheckedDivisions => Rows(
    [
        // A divisor that may be 0
        (SpecialType.System_Int32, s_int32, s_int32),
        (SpecialType.System_Int32, s_int32, s_byte),
        (SpecialType.System_Int32, s_int32, IntegerRange.Constant(0)),
        (SpecialType.System_UInt32, s_uint32, s_uint32),
        (SpecialType.System_Int64, s_int64, s_int64),
        (SpecialType.System_UInt64, s_uint64, new IntegerRange(0, ulong.MaxValue)),
        // -1 against a dividend that may be the minimum (the GPU result, which x86 idiv cannot compute)
        (SpecialType.System_Int32, s_int32, IntegerRange.Constant(-1)),
        (SpecialType.System_Int32, IntegerRange.Constant(int.MinValue), new IntegerRange(-5, -1)),
        (SpecialType.System_Int64, s_int64, IntegerRange.Constant(-1)),
    ]);

    public static TheoryData<SpecialType, BigInteger, BigInteger, BigInteger, BigInteger> PlainDivisions => Rows(
    [
        // A divisor that is neither 0 nor -1
        (SpecialType.System_Int32, s_int32, IntegerRange.Constant(7)),
        (SpecialType.System_Int32, s_int32, new IntegerRange(1, 255)),
        (SpecialType.System_Int32, IntegerRange.Constant(int.MinValue), IntegerRange.Constant(-2)),
        (SpecialType.System_UInt32, s_uint32, IntegerRange.Constant(3)),
        (SpecialType.System_UInt64, s_uint64, IntegerRange.Constant(ulong.MaxValue)),
        // -1 against a dividend that cannot be the minimum: a short widened to int, an int widened to long,
        // MinValue + 1
        (SpecialType.System_Int32, s_int16, IntegerRange.Constant(-1)),
        (SpecialType.System_Int64, s_int32, IntegerRange.Constant(-1)),
        (SpecialType.System_Int32, new IntegerRange(int.MinValue + 1L, int.MaxValue), new IntegerRange(-5, -1)),
        // Not an operator type of an integer division: a narrow type is promoted first, a float is IEEE division
        (SpecialType.System_Int16, s_int16, s_int16),
        (SpecialType.System_Double, s_int32, s_int32),
    ]);

    // mutant: Decide without "&& dividend.Contains(minimum)" checks the -1 divisors of PlainDivisions as well
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(CheckedDivisions))]
    public void Decide_DivisorThatMayFail_IsChecked(SpecialType type, BigInteger dividendMin, BigInteger dividendMax,
        BigInteger divisorMin, BigInteger divisorMax)
    {
        var check = Decide(type, dividendMin, dividendMax, divisorMin, divisorMax);

        Assert.Equal(DivisionCheck.Divisor, check);
        Assert.True(DivisionCheckRule.MayThrow(check));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(PlainDivisions))]
    public void Decide_DivisorThatCannotFail_KeepsTheOperator(SpecialType type, BigInteger dividendMin,
        BigInteger dividendMax, BigInteger divisorMin, BigInteger divisorMax)
    {
        var check = Decide(type, dividendMin, dividendMax, divisorMin, divisorMax);

        Assert.Equal(DivisionCheck.None, check);
        Assert.False(DivisionCheckRule.MayThrow(check));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(SpecialType.System_SByte, -128L, 127L)]
    [InlineData(SpecialType.System_Char, 0L, 65535L)]
    [InlineData(SpecialType.System_Int32, int.MinValue, int.MaxValue)]
    public void OfType_IntegerType_SpansTheType(SpecialType type, long min, long max) =>
        Assert.Equal(new IntegerRange(min, max), IntegerRange.OfType(type));

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void OfType_NotAnInteger_IsNull()
    {
        Assert.Null(IntegerRange.OfType(SpecialType.System_Single));
        Assert.Null(IntegerRange.OfType(SpecialType.System_IntPtr));
    }

    private static DivisionCheck Decide(SpecialType type, BigInteger dividendMin, BigInteger dividendMax,
        BigInteger divisorMin, BigInteger divisorMax) =>
        DivisionCheckRule.Decide(
            type,
            new IntegerRange(dividendMin, dividendMax),
            new IntegerRange(divisorMin, divisorMax)
        );

    // The rows as plain values: IntegerRange is internal, and a member that xUnit reads is public
    private static TheoryData<SpecialType, BigInteger, BigInteger, BigInteger, BigInteger> Rows(
        List<(SpecialType Type, IntegerRange Dividend, IntegerRange Divisor)> rows)
    {
        var data = new TheoryData<SpecialType, BigInteger, BigInteger, BigInteger, BigInteger>();
        foreach (var (type, dividend, divisor) in rows)
        {
            data.Add(type, dividend.Min, dividend.Max, divisor.Min, divisor.Max);
        }

        return data;
    }
}
