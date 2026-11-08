namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// The width rules of the big integer operations (bits in, bits out), checked against BigInteger: for each rule, the
// bound holds for the largest operands of the given bit lengths and is reached by them (it is the least such bound),
// except where the rule is fixed by the width of the operand (~a unsigned) or documented as an upper bound (a / b,
// a % b, sqrt).

/// <summary>Tests of <see cref="BigIntWidths"/>; no GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class BigIntWidthsTests
{
    public static TheoryData<long, int> WordCounts() => new()
    {
        { 0, 1 },
        { 1, 1 },
        { 31, 1 },
        { 32, 1 },
        { 33, 2 },
        { 64, 2 },
        { 65, 3 },
        { 32L * int.MaxValue, int.MaxValue }
    };

    [Theory]
    [MemberData(nameof(WordCounts))]
    [Scenario(TestScenario.Boundary)]
    public void Words_RoundsUp_AndIsAtLeastOne(long bits, int words) => Assert.Equal(words, BigIntWidths.Words(bits));

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Words_BeyondTheLargestBuffer_Overflows()
    {
        Assert.Throws<OverflowException>(() => BigIntWidths.Words(32L * int.MaxValue + 1));
        Assert.Throws<OverflowException>(() => BigIntWidths.Words(long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => BigIntWidths.OfWords(0));
        Assert.Equal(32L * 5, BigIntWidths.OfWords(5));
    }

    public static TheoryData<long, long> OperandBits() => new()
    {
        { 1, 1 },
        { 32, 32 },
        { 33, 32 },
        { 32, 64 },
        { 65, 1 },
        { 1056, 33 }
    };

    // The largest value of each bit length is the extreme case of every rule below
    private static BigInteger Max(long bits) => (BigInteger.One << (int)bits) - 1;

    [Theory]
    [MemberData(nameof(OperandBits))]
    [Scenario(TestScenario.Boundary)]
    public void Add_And_Multiply_AreTheLeastBounds(long a, long b)
    {
        Assert.Equal((long)(Max(a) + Max(b)).GetBitLength(), BigIntWidths.Add(a, b));
        Assert.Equal(BigIntWidths.Add(a, b), BigIntWidths.SignedSubtract(a, b));
        // A product bound of a + b bits holds always and is reached when both operands have two bits or more
        // ((2^a - 1)(2^b - 1) >= 2^(a + b - 1)); an operand of one bit is 1
        Assert.True((Max(a) * Max(b)).GetBitLength() <= BigIntWidths.Multiply(a, b));
        if (a >= 2 && b >= 2)
        {
            Assert.Equal((long)(Max(a) * Max(b)).GetBitLength(), BigIntWidths.Multiply(a, b));
        }
    }

    [Theory]
    [MemberData(nameof(OperandBits))]
    [Scenario(TestScenario.Boundary)]
    public void Division_Remainder_AndBitwise_Bounds(long a, long b)
    {
        // The quotient by 1 is the dividend; the remainder is below the divisor and at most the dividend
        Assert.Equal(a, BigIntWidths.Divide(a, b));
        Assert.Equal(a, BigIntWidths.UnsignedSubtract(a, b));
        Assert.Equal(Math.Min(a, b), BigIntWidths.Remainder(a, b));
        Assert.True((Max(a) % (Max(b) + 1)).GetBitLength() <= BigIntWidths.Remainder(a, b));

        Assert.Equal((long)(Max(a) & Max(b)).GetBitLength(), BigIntWidths.And(a, b));
        Assert.Equal((long)(Max(a) | Max(b)).GetBitLength(), BigIntWidths.Or(a, b));
        Assert.Equal(Math.Max(a, b), BigIntWidths.Xor(a, b));
    }

    public static TheoryData<long, long, long, long> Shifts() => new()
    {
        // a, count, a << count, a >> count
        { 64, 0, 64, 64 },
        { 64, 1, 65, 63 },
        { 64, 33, 97, 31 },
        { 64, 64, 128, 1 },
        { 64, 100, 164, 1 },
        { 64, -1, 63, 65 },
        { 64, -33, 31, 97 },
        { 64, int.MinValue, 1, 64 + 2147483648L },
        { 1, 1, 2, 1 }
    };

    [Theory]
    [MemberData(nameof(Shifts))]
    [Scenario(TestScenario.Boundary)]
    public void Shifts_GrowOrShrinkByTheCount_AndANegativeCountShiftsTheOtherWay(long a, long count, long left,
        long right)
    {
        Assert.Equal(left, BigIntWidths.ShiftLeft(a, count));
        Assert.Equal(right, BigIntWidths.ShiftRight(a, count));
        // The oracle: BigInteger shifts the largest value of a bits (a negative count shifts the other way)
        if (count is >= -1000 and <= 1000)
        {
            Assert.Equal(Math.Max(1L, (long)(Max(a) << (int)count).GetBitLength()), left);
            Assert.Equal(Math.Max(1L, (long)(Max(a) >> (int)count).GetBitLength()), right);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(32, 16)]
    [InlineData(33, 17)]
    [InlineData(64, 32)]
    [InlineData(65, 33)]
    [Scenario(TestScenario.Boundary)]
    public void Sqrt_IsHalfTheBitsRoundedUp(long a, long expected)
    {
        Assert.Equal(expected, BigIntWidths.Sqrt(a));
        Assert.True(BigIntegerSqrt(Max(a)).GetBitLength() <= expected);
    }

    [Theory]
    [InlineData(1, 2, 32)]
    [InlineData(32, 33, 32)]
    [InlineData(33, 34, 64)]
    [Scenario(TestScenario.Boundary)]
    public void Not_SignedGrowsByOneBit_UnsignedCoversTheWords(long a, long signed, long unsigned)
    {
        // ~x == -x - 1: the magnitude of ~Max(a) is Max(a) + 1
        Assert.Equal(signed, BigIntWidths.SignedNot(a));
        Assert.Equal((long)(BigInteger.Abs(~Max(a))).GetBitLength(), BigIntWidths.SignedNot(a));
        Assert.Equal(unsigned, BigIntWidths.UnsignedNot(a));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Bounds_ThatLeaveLong_Overflow()
    {
        Assert.Throws<OverflowException>(() => BigIntWidths.Add(long.MaxValue, 1));
        Assert.Throws<OverflowException>(() => BigIntWidths.Multiply(long.MaxValue, 1));
        Assert.Throws<OverflowException>(() => BigIntWidths.ShiftLeft(long.MaxValue, 1));
        Assert.Throws<OverflowException>(() => BigIntWidths.SignedNot(long.MaxValue));
    }

    private static BigInteger BigIntegerSqrt(BigInteger n)
    {
        if (n.IsZero)
        {
            return n;
        }

        var x = BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
        while (true)
        {
            var y = (x + n / x) >> 1;
            if (y >= x)
            {
                return x;
            }

            x = y;
        }
    }
}
