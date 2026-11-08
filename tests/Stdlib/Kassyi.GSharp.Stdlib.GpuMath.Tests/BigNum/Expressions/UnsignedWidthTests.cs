#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of the unsigned expression widths, against a BigInteger oracle at operand widths 1, 2, 33, 64 and 1025
// words and the unequal pairs between them:
// - every operator equals the oracle and its result has the words of its bound (BigIntWidths): a sum keeps its carry,
//   a product its top words, a left shift every bit; operands of different widths are accepted;
// - a difference below zero is a domain error, also when only a high word of the wider subtrahend makes it so;
// - a negative shift count shifts the other way, as BigInteger, in expressions and in GpuBigIntMath.ShiftLeft/Right;
// - a target narrower than the result is rejected before anything is written; a wider one receives the exact value and
//   zeros above it (leaf and operator roots, x.Value = y);
// - Truncate keeps the value modulo the width, Fit keeps it or reports a value that does not fit;
// - PadTo only widens.

/// <summary>Unsigned expression widths on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class UnsignedWidthTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public UnsignedWidthTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    public static TheoryData<int, int> WidthPairs() => UnsignedCases.WidthPairs();

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.Boundary)]
    public void Operators_FollowBigInteger_WithTheWordsOfTheirBound(int aWords, int bWords) =>
        UnsignedCases.Operators(_workspace, aWords, bWords);

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.SadPath)]
    public void Subtraction_BelowZero_IsADomainError(int aWords, int bWords)
    {
        foreach (var (a, b) in UnsignedCases.BelowZero(aWords, bWords))
        {
            using var x = UnsignedCases.Value(_workspace, a, aWords);
            using var y = UnsignedCases.Value(_workspace, b, bWords);
            using var result = (x.Value - y).Node.Evaluate(_workspace);
            Assert.Throws<OverflowException>(() => _workspace.ThrowIfDeviceErrors());
        }
    }

    [Theory]
    [MemberData(nameof(ShiftCounts))]
    [Scenario(TestScenario.Boundary)]
    public void Shifts_ByAnyCount_FollowBigInteger(int count) => UnsignedCases.Shifts(_workspace, count);

    public static TheoryData<int> ShiftCounts() => UnsignedCases.ShiftCounts();

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void DirectShifts_ByANegativeCount_ShiftTheOtherWay() => UnsignedCases.DirectShifts(_workspace);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Assignment_IntoAWiderValue_ClearsItsUpperWords() => UnsignedCases.Assignments(_workspace);

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void TruncateAndFit_NarrowAsUncheckedAndChecked()
    {
        UnsignedCases.Narrowing(_workspace);

        // Fit of a value that does not fit
        using var big = UnsignedCases.Value(_workspace, (BigInteger.One << 40) + 3, 2);
        using var fitted = big.Value.Fit(1).Node.Evaluate(_workspace);
        Assert.Throws<OverflowException>(() => _workspace.ThrowIfDeviceErrors());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void PadTo_OnlyWidens() => UnsignedCases.PadTo(_workspace);
}

/// <summary>Unsigned expression widths on the CPU fallback.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class UnsignedWidthCpuFallbackTests
{
    public static TheoryData<int, int> WidthPairs() => UnsignedCases.WidthPairs();

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.Boundary)]
    public void FollowsTheSameRules(int aWords, int bWords)
    {
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(1 << 16);
            UnsignedCases.Operators(workspace, aWords, bWords);
            foreach (var (a, b) in UnsignedCases.BelowZero(aWords, bWords))
            {
                using var x = UnsignedCases.Value(workspace, a, aWords);
                using var y = UnsignedCases.Value(workspace, b, bWords);
                Assert.Throws<OverflowException>(() => (x.Value - y).Node.Evaluate(workspace));
            }

            if (aWords == 2 && bWords == 2)
            {
                foreach (var count in new[] { -65, -33, -1, 0, 1, 33, 65, int.MinValue })
                {
                    UnsignedCases.Shifts(workspace, count);
                }

                UnsignedCases.DirectShifts(workspace);
                UnsignedCases.Assignments(workspace);
                UnsignedCases.Narrowing(workspace);
                UnsignedCases.PadTo(workspace);

                using var big = UnsignedCases.Value(workspace, (BigInteger.One << 40) + 3, 2);
                Assert.Throws<OverflowException>(() => big.Value.Fit(1).Node.Evaluate(workspace));
            }
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }
}

/// <summary>Inputs and oracle checks shared by the GPU and CPU fallback tests.</summary>
internal static class UnsignedCases
{
    private const uint Fill = 0xDEADBEEFu;

    private static readonly int[] s_widths = [1, 2, 33, 64, 1025];

    public static TheoryData<int, int> WidthPairs()
    {
        var data = new TheoryData<int, int>();
        foreach (var a in s_widths)
        {
            foreach (var b in s_widths)
            {
                data.Add(a, b);
            }
        }

        return data;
    }

    public static TheoryData<int> ShiftCounts() => new() { -65, -33, -32, -1, 0, 1, 31, 32, 33, 65, int.MinValue };

    public static BigInteger AllOnes(int bits) => (BigInteger.One << bits) - 1;

    public static GpuBigUInt Value(GpuWorkspace workspace, BigInteger value, int words)
    {
        var limbs = new uint[words];
        value.WriteLimbs(limbs);
        return new GpuBigUInt(workspace, limbs);
    }

    public static GpuBigUInt Filled(GpuWorkspace workspace, int words) =>
        new(workspace, Enumerable.Repeat(Fill, words).ToArray());

    public static uint[] Limbs(BigInteger value, int words)
    {
        var limbs = new uint[words];
        value.WriteLimbs(limbs);
        return limbs;
    }

    // a - b below zero: a smaller value, and a zero a with a b whose only set bit is in its top word
    public static IEnumerable<(BigInteger A, BigInteger B)> BelowZero(int aWords, int bWords)
    {
        yield return (BigInteger.Zero, BigInteger.One);
        yield return (AllOnes(32 * Math.Min(aWords, bWords) - 1), BigInteger.One << (32 * bWords - 1));
    }

    public static void Operators(GpuWorkspace workspace, int aWords, int bWords)
    {
        // a is all ones (carry chains through every word); b fills its words below the top bit and is not zero
        var a = AllOnes(32 * aWords);
        var b = AllOnes(32 * bWords - 1) - 12344;
        if (b.Sign <= 0)
        {
            b = 7;
        }

        using var x = Value(workspace, a, aWords);
        using var y = Value(workspace, b, bWords);
        long aBits = 32L * aWords;
        long bBits = 32L * bWords;

        Check(workspace, x.Value + y, a + b, BigIntWidths.Add(aBits, bBits));
        Check(workspace, x.Value * y, a * b, BigIntWidths.Multiply(aBits, bBits));
        Check(workspace, x.Value / y, a / b, BigIntWidths.Divide(aBits, bBits));
        Check(workspace, x.Value % y, a % b, BigIntWidths.Remainder(aBits, bBits));
        Check(workspace, x.Value & y, a & b, BigIntWidths.And(aBits, bBits));
        Check(workspace, x.Value | y, a | b, BigIntWidths.Or(aBits, bBits));
        Check(workspace, x.Value ^ y, a ^ b, BigIntWidths.Xor(aBits, bBits));
        Check(workspace, ~y.Value, AllOnes(32 * bWords) ^ b, BigIntWidths.UnsignedNot(bBits));
        Check(workspace, GpuBigUIntExpr.Sqrt(x.Value), Sqrt(a), BigIntWidths.Sqrt(aBits));

        // a - b with a the larger value, whatever the widths: the borrow chain runs through every word of a
        var larger = a >= b ? (x, a, y, b) : (y, b, x, a);
        Check(workspace, larger.Item1.Value - larger.Item3, larger.Item2 - larger.Item4,
            BigIntWidths.UnsignedSubtract(a >= b ? aBits : bBits, a >= b ? bBits : aBits));

        // A chain: (a + b) * (a + b) has fewer words than the sum of its operand words
        var sumBits = BigIntWidths.Add(aBits, bBits);
        Check(workspace, (x.Value + y) * (x.Value + y), (a + b) * (a + b), BigIntWidths.Multiply(sumBits, sumBits));
        workspace.ThrowIfDeviceErrors();
    }

    public static void Shifts(GpuWorkspace workspace, int count)
    {
        var value = (BigInteger)0x380000002L;
        using var x = Value(workspace, value, 2);
        var left = count == int.MinValue ? BigInteger.Zero : count >= 0 ? value << count : value >> -count;
        Check(workspace, x.Value << count, left, BigIntWidths.ShiftLeft(64, count));
        if (count != int.MinValue)
        {
            var right = count >= 0 ? value >> count : value << -count;
            Check(workspace, x.Value >> count, right, BigIntWidths.ShiftRight(64, count));
        }

        workspace.ThrowIfDeviceErrors();
    }

    public static void DirectShifts(GpuWorkspace workspace)
    {
        var value = (BigInteger)0x380000002L;
        var modulus = BigInteger.One << 64;
        using var x = Value(workspace, value, 2);
        using var r = new GpuBigUInt(workspace, 2);
        foreach (var count in new[] { -33, -1, 1, 33 })
        {
            GpuBigIntMath.ShiftLeft(x, count, r);
            Assert.Equal((count >= 0 ? value << count : value >> -count) % modulus, r.ToBigInteger());
            GpuBigIntMath.ShiftRight(x, count, r);
            Assert.Equal((count >= 0 ? value >> count : value << -count) % modulus, r.ToBigInteger());
        }

        GpuBigIntMath.ShiftLeft(x, int.MinValue, r);
        Assert.Equal(BigInteger.Zero, r.ToBigInteger());
        GpuBigIntMath.ShiftRight(x, int.MinValue, r);
        Assert.Equal(BigInteger.Zero, r.ToBigInteger());
    }

    public static void Assignments(GpuWorkspace workspace)
    {
        // The values of the issue: a two-word x receives a one-word value
        using var x = Value(workspace, (BigInteger.One << 32) + 1, 2);
        using var y = Value(workspace, 1, 1);
        using var z = Value(workspace, 2, 1);

        x.Value = y;
        Assert.Equal(BigInteger.One, x.ToBigInteger());

        x.Value = y.Value + z;
        Assert.Equal(new BigInteger(3), x.ToBigInteger());

        // A wider target prefilled: every word above the result is cleared; a narrower one is rejected unwritten
        using var wide = Filled(workspace, 5);
        (y.Value * z).Node.Evaluate(workspace, wide);
        Assert.Equal(Limbs(2, 5), wide.ToHostArray());

        using var narrow = Filled(workspace, 1);
        Assert.Throws<ArgumentException>(() => narrow.Value = x.Value + x);
        Assert.All(narrow.ToHostArray(), static w => Assert.Equal(Fill, w));
    }

    public static void Narrowing(GpuWorkspace workspace)
    {
        var value = (BigInteger.One << 40) + 3;
        using var big = Value(workspace, value, 2);

        using var truncated = big.Value.Truncate(1).Node.Evaluate(workspace);
        Assert.Equal(1, truncated.NumWords);
        Assert.Equal(new BigInteger(3), truncated.ToBigInteger());

        using var widened = big.Value.Truncate(4).Node.Evaluate(workspace);
        Assert.Equal(value, widened.ToBigInteger());

        using var fits = (big.Value - big).Fit(1).Node.Evaluate(workspace);
        Assert.Equal(BigInteger.Zero, fits.ToBigInteger());
        workspace.ThrowIfDeviceErrors();
        Assert.Throws<ArgumentOutOfRangeException>(() => big.Value.Fit(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => big.Value.Truncate(0));
    }

    public static void PadTo(GpuWorkspace workspace)
    {
        using var v = Value(workspace, (BigInteger.One << 32) + 7, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => v.PadTo(1));
        using var padded = v.PadTo(4);
        Assert.Equal((BigInteger.One << 32) + 7, padded.ToBigInteger());
        Assert.Equal(4, padded.NumWords);
    }

    private static void Check(GpuWorkspace workspace, GpuBigUIntExpr expression, BigInteger expected, long bits)
    {
        Assert.Equal(BigIntWidths.Words(bits), expression.Node.NumWords);
        using var result = expression.Node.Evaluate(workspace);
        Assert.Equal(BigIntWidths.Words(bits), result.NumWords);
        Assert.Equal(expected, result.ToBigInteger());
    }

    private static BigInteger Sqrt(BigInteger n)
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
