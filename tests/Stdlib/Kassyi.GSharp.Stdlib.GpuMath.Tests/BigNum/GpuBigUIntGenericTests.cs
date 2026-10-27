
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[Category(TestCategory.Stdlib)]
public class GpuBigUIntGenericTests
{
    [Fact]
    public void Test_GpuBigUInt_Constants()
    {
        var zero = GpuBigUInt<LimbBuffer4>.Zero;
        Assert.True(zero.IsZero());

        var one = GpuBigUInt<LimbBuffer4>.One;
        Assert.False(one.IsZero());
        Assert.Equal(1u, one.Limbs[0]);
        Assert.Equal(0u, one.Limbs[1]);
    }

    [Fact]
    public void Test_GpuBigUInt_Addition()
    {
        var a = GpuBigUInt<LimbBuffer4>.One;
        var b = GpuBigUInt<LimbBuffer4>.One;
        var c = a + b;
        
        Assert.Equal(2u, c.Limbs[0]);
        Assert.Equal(0u, c.Limbs[1]);
        
        // Test carry
        a.Limbs[0] = uint.MaxValue;
        b.Limbs[0] = 1;
        var d = a + b;
        
        Assert.Equal(0u, d.Limbs[0]);
        Assert.Equal(1u, d.Limbs[1]);
    }

    [Fact]
    public void Test_GpuBigUInt_Subtraction()
    {
        var al = new LimbBuffer4(); al[0] = 5;
        var bl = new LimbBuffer4(); bl[0] = 3;
        var a = new GpuBigUInt<LimbBuffer4>(al);
        var b = new GpuBigUInt<LimbBuffer4>(bl);
        var c = a - b;
        
        Assert.Equal(2u, c.Limbs[0]);
        Assert.Equal(0u, c.Limbs[1]);
        
        // Test borrow
        a.Limbs[0] = 0;
        a.Limbs[1] = 1;
        b.Limbs[0] = 1;
        b.Limbs[1] = 0;
        var d = a - b;
        
        Assert.Equal(uint.MaxValue, d.Limbs[0]);
        Assert.Equal(0u, d.Limbs[1]);
    }

    [Fact]
    public void Test_GpuBigUInt_Comparison()
    {
        var al = new LimbBuffer4(); al[0] = 5;
        var bl = new LimbBuffer4(); bl[0] = 3;
        var cl = new LimbBuffer4(); cl[0] = 5;
        var a = new GpuBigUInt<LimbBuffer4>(al);
        var b = new GpuBigUInt<LimbBuffer4>(bl);
        var c = new GpuBigUInt<LimbBuffer4>(cl);
        
        Assert.True(a > b);
        Assert.True(a >= b);
        Assert.True(b < a);
        Assert.True(b <= a);
        Assert.True(a == c);
        Assert.True(a >= c);
        Assert.True(a <= c);
        Assert.True(a != b);
    }

    [Fact]
    public void Test_GpuBigUInt_ShiftLeft()
    {
        var al = new LimbBuffer4(); al[0] = 1;
        var a = new GpuBigUInt<LimbBuffer4>(al);
        var b = a.ShiftLeft(1);
        Assert.Equal(2u, b.Limbs[0]);
        
        var c = a.ShiftLeft(32);
        Assert.Equal(0u, c.Limbs[0]);
        Assert.Equal(1u, c.Limbs[1]);
    }

    [Fact]
    public void Test_GpuBigUInt_ShiftRight()
    {
        var al = new LimbBuffer4(); al[0] = 0; al[1] = 1;
        var a = new GpuBigUInt<LimbBuffer4>(al);
        var b = a.ShiftRight(32, out bool stickyBit);
        Assert.Equal(1u, b.Limbs[0]);
        Assert.Equal(0u, b.Limbs[1]);
        Assert.False(stickyBit);
    }

    [Fact]
    public void Test_GpuBigUInt_SetMsb()
    {
        var a = new GpuBigUInt<LimbBuffer4>();
        var b = a.SetMsb();
        Assert.Equal(0x80000000u, b.Limbs[3]);
    }

    [Fact]
    public void Test_GpuBigUInt_IsOdd()
    {
        var al = new LimbBuffer4(); al[0] = 5;
        var bl = new LimbBuffer4(); bl[0] = 4;
        var a = new GpuBigUInt<LimbBuffer4>(al);
        var b = new GpuBigUInt<LimbBuffer4>(bl);
        
        Assert.True(a.IsOdd());
        Assert.False(b.IsOdd());
    }

    [Fact]
    public void Test_GpuBigUInt_CountLeadingZeros()
    {
        var al = new LimbBuffer4(); al[0] = 1;
        var a = new GpuBigUInt<LimbBuffer4>(al);
        int leading = a.CountLeadingZeros();
        Assert.Equal(127, leading); // 4 * 32 - 1
    }

    // ---- BigInteger reference properties (#598 stage 2b) ----

    private static readonly BigInteger s_modulus = BigInteger.One << 128;

    // Every shift position class: no shift, the guard / round / sticky positions inside limb 0 (1, 2, 3), the limb
    // boundaries where the round bit sits at the top of the limb below (33, 65, 97: s % 32 == 1), whole limbs (32, 64,
    // 96, 128) and shifts that discard every bit (128 and beyond).
    private static readonly int[] s_shiftRightAmounts = [0, 1, 2, 3, 31, 32, 33, 34, 40, 63, 64, 65, 66, 96, 97, 127, 128, 129, 130, 160];

    private static readonly int[] s_shiftLeftAmounts = [0, 1, 31, 32, 33, 40, 64, 100, 127, 128, 200];

    /// <summary>
    /// 128-bit values (limb 0 first) that reach the limb-boundary and bit-boundary branches of the shifts:
    /// top bit of a limb, the bit below it, words that are only 1, all four limbs set, and seeded random values.
    /// </summary>
    public static TheoryData<uint, uint, uint, uint> ShiftValues()
    {
        var data = new TheoryData<uint, uint, uint, uint>
        {
            { 0xC000_0001u, 0u, 0u, 0u },
            { 0x4000_0000u, 0u, 0u, 0u },
            { 0x8000_0000u, 0u, 0u, 0u },
            // Only bit 63 set: the round bit of a 65-bit shift is the top bit of limb 1 (#639).
            { 0u, 0x8000_0000u, 0u, 0u },
            { 1u, 1u, 0u, 0u },
            { 1u, 1u, 1u, 0u },
            { 0x1234_5678u, 0x9ABC_DEF0u, 0x0FED_CBA9u, 0x8765_4321u },
            // Low limb equal to the bits that the 40-bit shift moves out of limb 1 below the round bit: sticky must OR them.
            { 0x0100_0000u, 1u, 0u, 0u },
        };

        // Seed 598 (the issue number) keeps the values stable across runs.
        var random = new Random(598);
        for (var i = 0; i < 4; i++)
        {
            data.Add(NextUInt(random), NextUInt(random), NextUInt(random), NextUInt(random));
        }

        return data;
    }

    /// <summary>
    /// Operands for addition, subtraction and comparison: 0, 1, small values, single-limb and limb-boundary values,
    /// the maximum 128-bit value and values whose borrow crosses a limb.
    /// </summary>
    public static TheoryData<int, int> OperandPairs()
    {
        var data = new TheoryData<int, int>();
        for (var a = 0; a < s_operands.Length; a++)
        {
            for (var b = 0; b < s_operands.Length; b++)
            {
                data.Add(a, b);
            }
        }

        return data;
    }

    private static readonly uint[][] s_operands =
    [
        [0u, 0u, 0u, 0u],
        [1u, 0u, 0u, 0u],
        [5u, 0u, 0u, 0u],
        [uint.MaxValue, 0u, 0u, 0u],
        [0u, 1u, 0u, 0u],
        [0u, 0u, 1u, 0u],
        [uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue],
        [0u, 5u, 0u, 0u],
        [1u, 1u, 0u, 0u],
        [0x8000_0000u, 0u, 0u, 0x8000_0000u],
    ];

    [Theory]
    [MemberData(nameof(ShiftValues))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ShiftRight_MatchesBigIntegerReference(uint l0, uint l1, uint l2, uint l3)
    {
        uint[] limbs = [l0, l1, l2, l3];
        var value = ToBigInteger(limbs);
        var source = FromLimbs(limbs);

        foreach (var shift in s_shiftRightAmounts)
        {
            var result = source.ShiftRight(shift, out var guard, out var round, out var sticky);

            // The quotient is floor(v / 2^s); guard is bit s-1, round is bit s-2 and sticky is (v mod 2^(s-2)) != 0.
            // Positions below 0 and above bit 127 are 0, so a shift by 128 or more gives 0 and the flags read the bits
            // that exist.
            // mutant: GpuBigUIntGeneric.cs:206 `TestBit(src, bits - 2)` -> `TestBit(src, bits - 1)` (random values, s = 2)
            // mutant: GpuBigUIntGeneric.cs:207 `AnyBitBelow(src, bits - 2)` -> `AnyBitBelow(src, bits - 1)` (value [0, 0x8000_0000, 0, 0], s = 65)
            // mutant: GpuBigUIntGeneric.cs:205 `bits >= 1` -> `bits >= 0` (value [0x8000_0000, 0, 0, 0], s = 0)
            // mutant: GpuBigUIntGeneric.cs:238 `val << (32 - bitShift)` -> `val >> (32 - bitShift)` (random values, s = 1)
            // mutant: GpuBigUIntGeneric.cs:248 `limb < src.Length` -> `limb <= src.Length` (s = 129: index out of range)
            // mutant: GpuBigUIntGeneric.cs:257 `src[i] != 0` -> `src[i] == 0` (random values, s = 34)
            // mutant: GpuBigUIntGeneric.cs:263 `(1u << (count % 32)) - 1u` -> `1u << (count % 32)` (random values, s = 3)
            var expected = (shift, value >> shift, Bit(value, shift - 1), Bit(value, shift - 2), shift > 2 && !(value % (BigInteger.One << (shift - 2))).IsZero);
            Assert.Equal(expected, (shift, ToBigInteger(result), guard, round, sticky));

            // The sticky-only overload returns the same quotient, and its flag is set when any discarded bit is 1:
            // (v mod 2^s) != 0.
            // mutant: GpuBigUIntGeneric.cs:184 `guardBit || roundBit || lowerBits` -> `lowerBits` (s = 1, 2, 3)
            var quotientOnly = source.ShiftRight(shift, out bool anyDiscarded);
            Assert.Equal(
                (shift, value >> shift, shift > 0 && !(value % (BigInteger.One << shift)).IsZero),
                (shift, ToBigInteger(quotientOnly), anyDiscarded)
            );
        }
    }

    /// <summary>
    /// Shifts that discard exactly one set bit, at the guard, round and sticky positions, and a whole limb whose only
    /// set bit is its top bit (#639).
    /// </summary>
    public static TheoryData<uint, int> SingleDiscardedBit() => new()
    {
        { 1u, 1 },
        { 2u, 2 },
        { 4u, 3 },
        { 0x8000_0000u, 32 },
    };

    [Theory]
    [MemberData(nameof(SingleDiscardedBit))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ShiftRight_StickyOverload_ReportsAnyDiscardedBit(uint low, int shift)
    {
        var source = FromLimbs([low, 0u, 0u, 0u]);

        // The only set bit is shifted out, so the quotient is 0 and the inexact flag is set whichever of guard, round
        // or sticky the bit lands in.
        // mutant: GpuBigUIntGeneric.cs:184 `guardBit || roundBit || lowerBits` -> `lowerBits` (all four inputs)
        var result = source.ShiftRight(shift, out bool anyDiscarded);
        Assert.Equal((BigInteger.Zero, true), (ToBigInteger(result), anyDiscarded));
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.SadPath)]
    public void ShiftRight_NegativeBits_Throws()
    {
        var source = FromLimbs([1u, 2u, 3u, 4u]);

        // A negative shift count has no meaning; both overloads reject it instead of masking it to a positive count.
        // mutant: GpuBigUIntGeneric.cs:203 `ThrowIfNegative(bits)` removed (-1 masks to a 31-bit shift and returns a value)
        Assert.Throws<ArgumentOutOfRangeException>(() => source.ShiftRight(-1, out _, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.ShiftRight(-33, out bool _));
    }

    [Theory]
    [MemberData(nameof(ShiftValues))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ShiftLeft_MatchesBigIntegerReference(uint l0, uint l1, uint l2, uint l3)
    {
        uint[] limbs = [l0, l1, l2, l3];
        var value = ToBigInteger(limbs);
        var source = FromLimbs(limbs);

        foreach (var shift in s_shiftLeftAmounts)
        {
            // The result is (v << s) mod 2^128.
            // mutant: GpuBigUIntGeneric.cs:293 `val >> (32 - bitShift)` -> `val << (32 - bitShift)` (value [0x8000_0000, 0, 0, 0], s = 1)
            // mutant: GpuBigUIntGeneric.cs:289 `src.Length - limbShift` -> `src.Length + limbShift` (s = 40, index out of range)
            var expected = (value << shift) % s_modulus;
            Assert.Equal((shift, expected), (shift, ToBigInteger(source.ShiftLeft(shift))));
        }
    }

    [Theory]
    [MemberData(nameof(OperandPairs))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void AddSubtract_MatchBigIntegerModulo(int leftIndex, int rightIndex)
    {
        var left = ToBigInteger(s_operands[leftIndex]);
        var right = ToBigInteger(s_operands[rightIndex]);
        var a = FromLimbs(s_operands[leftIndex]);
        var b = FromLimbs(s_operands[rightIndex]);

        // The sum is (a + b) mod 2^128, and the carry is set exactly when a + b overflows 128 bits.
        // mutant: GpuBigUIntGeneric.cs:136 `c != 0` -> `c == 0`
        var sum = a.Add(b, out var carry);
        Assert.Equal(((left + right) % s_modulus, left + right >= s_modulus), (ToBigInteger(sum), carry));
        Assert.Equal((left + right) % s_modulus, ToBigInteger(a + b));

        // The difference is (a - b) mod 2^128, and the borrow is set exactly when a < b.
        // mutant: GpuBigUIntGeneric.cs:157 `diff < 0` -> `diff <= 0` ([0, 1, 0, 0] - [0, 1, 0, 0])
        // mutant: GpuBigUIntGeneric.cs:163 else block removed ([0, 5, 0, 0] - [1, 1, 0, 0])
        var difference = a.Subtract(b, out var borrow);
        var expectedDifference = ((left - right) % s_modulus + s_modulus) % s_modulus;
        Assert.Equal((expectedDifference, left < right), (ToBigInteger(difference), borrow));
        Assert.Equal(expectedDifference, ToBigInteger(a - b));
    }

    [Theory]
    [MemberData(nameof(OperandPairs))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Comparison_MatchesBigInteger(int leftIndex, int rightIndex)
    {
        var left = ToBigInteger(s_operands[leftIndex]);
        var right = ToBigInteger(s_operands[rightIndex]);
        var a = FromLimbs(s_operands[leftIndex]);
        var b = FromLimbs(s_operands[rightIndex]);

        // Each operator agrees with BigInteger; the pairs include a == b, where > and < must be false.
        // mutant: GpuBigUIntGeneric.cs:86 `> 0` -> `>= 0` (a == b)
        // mutant: GpuBigUIntGeneric.cs:95 `< 0` -> `<= 0` (a == b)
        Assert.Equal(
            (left > right, left < right, left >= right, left <= right, left == right, left != right, left.CompareTo(right)),
            (a > b, a < b, a >= b, a <= b, a == b, a != b, Math.Sign(a.CompareTo(b)))
        );
    }

    [Theory]
    [MemberData(nameof(ShiftValues))]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void SetMsb_IsIdempotent_AndFlagsAreUnsigned(uint l0, uint l1, uint l2, uint l3)
    {
        uint[] limbs = [l0, l1, l2, l3];
        var value = ToBigInteger(limbs);
        var source = FromLimbs(limbs);

        // SetMsb ORs bit 127 in and keeps every other bit; applying it twice changes nothing.
        // mutant: GpuBigUIntGeneric.cs:310 `|=` -> `^=` (values whose bit 127 is already set)
        var once = source.SetMsb();
        Assert.Equal(value | (BigInteger.One << 127), ToBigInteger(once));
        Assert.Equal(ToBigInteger(once), ToBigInteger(once.SetMsb()));

        // An unsigned value is never negative.
        // mutant: GpuBigUIntGeneric.cs:21 / :24 `false` -> `true`
        Assert.False(source.IsNegative);
        Assert.False(source.CanBeNegative);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void Builder_FillsUpToCapacity_AndRejectsMore()
    {
        // Up to four limbs fit into LimbBuffer4; the missing high limbs are zero.
        var full = GpuBigUIntBuilder.Create<LimbBuffer4>([1u, 2u, 3u, 4u]);
        Assert.Equal(ToBigInteger(new uint[] { 1u, 2u, 3u, 4u }), ToBigInteger(full));
        var partial = GpuBigUIntBuilder.Create<LimbBuffer4>([7u]);
        Assert.Equal(new BigInteger(7), ToBigInteger(partial));

        // A fifth limb does not fit and is rejected by the builder's own check, which names the capacity.
        // Without that check Span.CopyTo also throws ArgumentException ("Destination is too short"), so the
        // message tells the two apart.
        // mutant: GpuBigUIntBuilder.cs:23 `>` -> `>=` (four limbs) / throw removed (five limbs)
        var ex = Assert.Throws<ArgumentException>(() => GpuBigUIntBuilder.Create<LimbBuffer4>([1u, 2u, 3u, 4u, 5u]));
        Assert.Contains("hold 4 elements", ex.Message);
    }

    private static uint NextUInt(Random random) => (uint)random.NextInt64(0, 1L << 32);

    private static bool Bit(BigInteger value, int position) => position >= 0 && !((value >> position) & BigInteger.One).IsZero;

    private static GpuBigUInt<LimbBuffer4> FromLimbs(uint[] limbs)
    {
        var buffer = new LimbBuffer4();
        for (var i = 0; i < 4; i++)
        {
            buffer[i] = limbs[i];
        }

        return new GpuBigUInt<LimbBuffer4>(buffer);
    }

    private static BigInteger ToBigInteger(uint[] limbs)
    {
        var value = BigInteger.Zero;
        for (var i = limbs.Length - 1; i >= 0; i--)
        {
            value = (value << 32) | limbs[i];
        }

        return value;
    }

    private static BigInteger ToBigInteger(GpuBigUInt<LimbBuffer4> value)
    {
        var limbs = new uint[4];
        for (var i = 0; i < 4; i++)
        {
            limbs[i] = value.Limbs[i];
        }

        return ToBigInteger(limbs);
    }
}
