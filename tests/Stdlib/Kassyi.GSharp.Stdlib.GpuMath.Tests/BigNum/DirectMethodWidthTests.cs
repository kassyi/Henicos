#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// Properties of the result buffers of the direct methods (GpuBigIntMath, GpuBigIntDivRem), against a BigInteger oracle
// computed from the host inputs, at operand widths 1, 2, 33 and 64 words and the unequal pairs between them:
// - operands may have different widths; a missing word reads as zero;
// - a result buffer narrower than the minimum of the method is rejected before anything is written;
// - a result buffer at or above the bound receives the exact value and zeros above it (pre-filled with 0xDEADBEEF);
// - a result buffer of the operand width (add, subtract, shift left) is the checked form: a lost carry, a borrow or
//   shifted-out bits are a domain error (OverflowException at the next read on the GPU, at the call on the CPU
//   fallback), and a value that fits is exact without an error.

/// <summary>Direct-method result widths on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class DirectMethodWidthTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public DirectMethodWidthTests()
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

    public static TheoryData<int, int> WidthPairs() => DirectCases.WidthPairs();

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.Boundary)]
    public void AddSubtract_AcceptDifferentWidths_AndCheckTheOperandWidth(int aWords, int bWords) =>
        DirectCases.AddSubtract(_workspace, aWords, bWords);

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.Boundary)]
    public void AddSignedMagnitude_AcceptsDifferentWidths(int aWords, int bWords) =>
        DirectCases.AddSignedMagnitude(_workspace, aWords, bWords);

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.Boundary)]
    public void MulAndBitwise_AcceptDifferentWidths(int aWords, int bWords) =>
        DirectCases.MulAndBitwise(_workspace, aWords, bWords);

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.Boundary)]
    public void DivisionFamily_AcceptsDifferentWidths(int aWords, int bWords) =>
        DirectCases.DivisionFamily(_workspace, aWords, bWords);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(33)]
    [InlineData(64)]
    [Scenario(TestScenario.Boundary)]
    public void Shifts_FollowTheirBounds(int aWords) => DirectCases.Shifts(_workspace, aWords);

    // A failed division leaves a wider quotient buffer as it was: the words above the quotient are cleared after it
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntMath.cs DivideByUInt32Core with
    // `ResultBuffers.ClearAbove` before the division (words 1 and 2 of the buffer are cleared)
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void DivideByUInt32_ByZero_LeavesAWiderQuotientUntouched()
    {
        using var a = new GpuBigUInt(_workspace, [100u]);
        uint[] filled = [0xDEADBEEFu, 0xDEADBEEFu, 0x80000000u];
        using var q = new GpuBigUInt(_workspace, filled);

        Assert.Throws<DivideByZeroException>(() => GpuBigIntMath.DivideByUInt32(a, 0u, q));
        Assert.Equal(filled, q.ToHostArray());
    }

    // A quotient buffer whose high word is the dividend: the result is that of separate buffers
    // mutant: the same, and the staging temporary removed (the quotient is 0, the remainder 0)
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void DivideByUInt32_IntoABufferThatHoldsTheDividend_IsExact()
    {
        using var q = new GpuBigUInt(_workspace, [0u, 100u]);
        var dividend = new GpuBigUInt(_workspace, q.Handle.Slice(1, 1));

        var remainder = GpuBigIntMath.DivideByUInt32(dividend, 7u, q);
        Assert.Equal(2u, remainder);
        Assert.Equal(new BigInteger(14), q.ToBigInteger());
    }

    // The callers of the unchecked shift drop only zero bits. Debug builds (the test runs) check each call on the
    // device, so a broken claim is an OverflowException here: divisors whose leading zeros are 0, 31 and 63 bits, the
    // moduli 1 and 2^(32 n - 1) on both ModPow paths, and square roots around the word boundaries.
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(40)]
    [Scenario(TestScenario.Boundary)]
    public void UncheckedShiftCallers_DropOnlyZeroBits(int words)
    {
        var top = 32 * words;
        foreach (var bits in new[] { top, top - 31, top - 63 })
        {
            var b = BigInteger.One << (bits - 1) | 3;
            var a = (BigInteger.One << (2 * top - 1)) - 12345;
            using var gpuA = new GpuBigUInt(_workspace, DirectLimbs(a, 2 * words));
            using var gpuB = new GpuBigUInt(_workspace, DirectLimbs(b, words));
            using var q = new GpuBigUInt(_workspace, 2 * words);
            using var r = new GpuBigUInt(_workspace, words);
            GpuBigIntDivRem.DivRem(gpuA, gpuB, q, r);
            _workspace.ThrowIfDeviceErrors();
            Assert.Equal(a / b, q.ToBigInteger());
            Assert.Equal(a % b, r.ToBigInteger());
        }

        foreach (var modulus in new[] { BigInteger.One, BigInteger.One << (top - 1), (BigInteger.One << top) - 1 })
        {
            var baseValue = (BigInteger.One << (top - 2)) + 7;
            using var gpuBase = new GpuBigUInt(_workspace, DirectLimbs(baseValue % modulus, words));
            using var exponent = new GpuBigUInt(_workspace, DirectLimbs(65537, words));
            using var gpuModulus = new GpuBigUInt(_workspace, DirectLimbs(modulus, words));
            using var result = new GpuBigUInt(_workspace, words);
            GpuBigIntDivRem.ModPow(gpuBase, exponent, gpuModulus, result);
            _workspace.ThrowIfDeviceErrors();
            Assert.Equal(BigInteger.ModPow(baseValue, 65537, modulus), result.ToBigInteger());
        }

        foreach (var bits in new[] { 33, 64, top - 1, top })
        {
            var value = (BigInteger.One << bits) - 1;
            using var gpuValue = new GpuBigUInt(_workspace, DirectLimbs(value, words));
            using var root = new GpuBigUInt(_workspace, words);
            GpuBigIntDivRem.Sqrt(gpuValue, root);
            _workspace.ThrowIfDeviceErrors();
            var s = root.ToBigInteger();
            Assert.True(s * s <= value && (s + 1) * (s + 1) > value, $"sqrt of {bits} bits");
        }
    }

    private static uint[] DirectLimbs(BigInteger value, int words)
    {
        var limbs = new uint[words];
        value.WriteLimbs(limbs);
        return limbs;
    }

    // The internal unchecked shift of the normalization paths: the value shifted by its leading zeros is exact in the
    // operand width and nothing is reported; another width or a negative count is rejected
    [Theory]
    [InlineData(1)]
    [InlineData(33)]
    [Scenario(TestScenario.Boundary)]
    public void UncheckedShift_NormalizesInTheOperandWidth(int aWords)
    {
        var value = BigInteger.One << (32 * aWords - 7) | 5;
        var limbs = new uint[aWords];
        value.WriteLimbs(limbs);
        using var a = new GpuBigUInt(_workspace, limbs);
        using var r = new GpuBigUInt(_workspace, aWords);

        GpuBigIntMath.ShiftLeftUnchecked(a, 6, r);
        _workspace.ThrowIfDeviceErrors();
        Assert.Equal(value << 6, r.ToBigInteger());

        using var wider = new GpuBigUInt(_workspace, aWords + 1);
        Assert.Throws<ArgumentException>(() => GpuBigIntMath.ShiftLeftUnchecked(a, 6, wider));
        Assert.Throws<ArgumentException>(() => GpuBigIntMath.ShiftLeftUnchecked(a, -1, r));
    }
}

/// <summary>Direct-method result widths on the CPU fallback.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class DirectMethodWidthCpuFallbackTests
{
    public static TheoryData<int, int> WidthPairs() => DirectCases.WidthPairs();

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.Boundary)]
    public void FollowsTheSameRules(int aWords, int bWords)
    {
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(1 << 16);
            DirectCases.AddSubtract(workspace, aWords, bWords);
            DirectCases.AddSignedMagnitude(workspace, aWords, bWords);
            DirectCases.MulAndBitwise(workspace, aWords, bWords);
            DirectCases.DivisionFamily(workspace, aWords, bWords);
            DirectCases.Shifts(workspace, aWords);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }
}

/// <summary>Inputs and oracle checks shared by the GPU and CPU fallback tests.</summary>
internal static class DirectCases
{
    private const uint Fill = 0xDEADBEEFu;
    private static readonly int[] s_widths = [1, 2, 33, 64];
    private static readonly List<GpuBigUInt> s_operands = [];

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

    public static void AddSubtract(GpuWorkspace workspace, int aWords, int bWords)
    {
        var wide = Math.Max(aWords, bWords);
        var allOnesA = AllOnes(32 * aWords);
        var allOnesB = AllOnes(32 * bWords);

        // All ones + all ones carries out of the wider operand: exact one word wider, zeros above in a wider buffer
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntMath.cs AddCore -> provider Add with
        // the operand width (the wider buffer keeps 0xDEADBEEF above, the narrow operand is read past its end)
        ExpectExact(workspace, allOnesA + allOnesB, wide + 3,
            r => GpuBigIntMath.Add(Value(workspace, allOnesA, aWords), Value(workspace, allOnesB, bWords), r));

        // The same sum into the operand width is the checked form; one that fits is exact
        ExpectDomainError(workspace, wide,
            r => GpuBigIntMath.Add(Value(workspace, allOnesA, aWords), Value(workspace, allOnesB, bWords), r));
        ExpectExact(workspace, allOnesA, wide,
            r => GpuBigIntMath.Add(Value(workspace, allOnesA - 1, aWords), Value(workspace, 2 - 1, bWords), r));

        // Narrower than the wider operand: rejected, nothing written
        if (wide > 1)
        {
            ExpectRejected(workspace, wide - 1,
                r => GpuBigIntMath.Add(Value(workspace, 1, aWords), Value(workspace, 1, bWords), r));
        }

        // a - b with a wide subtrahend whose only non-zero word is above the minuend: a borrow, a domain error
        if (bWords > aWords)
        {
            var high = BigInteger.One << (32 * (bWords - 1));
            ExpectDomainError(workspace, aWords,
                r => GpuBigIntMath.Sub(Value(workspace, allOnesA, aWords), Value(workspace, high, bWords), r));
        }

        // a - b that fits: exact, also into a wider buffer
        var small = AllOnes(32 * Math.Min(aWords, bWords) - 1);
        ExpectExact(workspace, allOnesA - small, aWords + 2,
            r => GpuBigIntMath.Sub(Value(workspace, allOnesA, aWords), Value(workspace, small, bWords), r));
        ExpectDomainError(workspace, aWords,
            r => GpuBigIntMath.Sub(Value(workspace, small, aWords), Value(workspace, small + 1, bWords), r));
        if (aWords > 1)
        {
            ExpectRejected(workspace, aWords - 1,
                r => GpuBigIntMath.Sub(Value(workspace, 2, aWords), Value(workspace, 1, bWords), r));
        }
    }

    public static void AddSignedMagnitude(GpuWorkspace workspace, int aWords, int bWords)
    {
        var a = AllOnes(32 * aWords);
        var b = AllOnes(32 * bWords - 1);
        var wide = Math.Max(aWords, bWords);
        foreach (var (aNegative, bNegative) in new[] { (false, false), (true, true), (false, true), (true, false) })
        {
            var expected = (aNegative ? -a : a) + (bNegative ? -b : b);
            using var r = Filled(workspace, wide + 1);
            using var gpuA = new GpuBigUInt(workspace, Limbs(a, aWords));
            using var gpuB = new GpuBigUInt(workspace, Limbs(b, bWords));
            var negative = GpuBigIntMath.AddSignedMagnitude(gpuA, aNegative, gpuB, bNegative, r);
            workspace.ThrowIfDeviceErrors();

            // The magnitude and the sign of the oracle; a sum of magnitudes keeps its carry in a buffer one word wider
            Assert.True(BigInteger.Abs(expected) == r.ToBigInteger(), $"{aWords}/{bWords} {aNegative}/{bNegative}");
            Assert.Equal(expected.Sign < 0, negative);
        }

        ExpectRejected(workspace, wide - 1,
            r => GpuBigIntMath.AddSignedMagnitude(Value(workspace, a, aWords), false, Value(workspace, b, bWords),
                true, r));
    }

    public static void MulAndBitwise(GpuWorkspace workspace, int aWords, int bWords)
    {
        var a = Random(aWords, 1);
        var b = Random(bWords, 2);
        var narrow = Math.Min(aWords, bWords);
        var wide = Math.Max(aWords, bWords);

        // The product into a buffer wider than both operands together: exact, zeros above
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntMath.cs MulCore `<` -> `!=` (rejected)
        ExpectExact(workspace, a * b, aWords + bWords + 2,
            r => GpuBigIntMath.Mul(Value(workspace, a, aWords), Value(workspace, b, bWords), r));
        ExpectRejected(workspace, aWords + bWords - 1,
            r => GpuBigIntMath.Mul(Value(workspace, a, aWords), Value(workspace, b, bWords), r));

        // & needs the narrower width, | and ^ the wider one
        ExpectExact(workspace, a & b, narrow,
            r => GpuBigIntMath.BitwiseAnd(Value(workspace, a, aWords), Value(workspace, b, bWords), r));
        ExpectExact(workspace, a | b, wide + 1,
            r => GpuBigIntMath.BitwiseOr(Value(workspace, a, aWords), Value(workspace, b, bWords), r));
        ExpectExact(workspace, a ^ b, wide,
            r => GpuBigIntMath.BitwiseXor(Value(workspace, a, aWords), Value(workspace, b, bWords), r));
        if (wide > narrow)
        {
            ExpectRejected(workspace, wide - 1,
                r => GpuBigIntMath.BitwiseOr(Value(workspace, a, aWords), Value(workspace, b, bWords), r));
        }

        // IsLessThan compares values of any widths
        using var gpuA = new GpuBigUInt(workspace, Limbs(a, aWords));
        using var gpuB = new GpuBigUInt(workspace, Limbs(b, bWords));
        Assert.Equal(a < b, GpuBigIntMath.IsLessThan(gpuA, gpuB));
    }

    public static void DivisionFamily(GpuWorkspace workspace, int aWords, int bWords)
    {
        var a = Random(aWords, 3);
        var b = Random(bWords, 4) | 1;

        // The quotient needs the dividend's width, the remainder the narrower one
        ExpectExact(workspace, a / b, aWords + 1,
            r => GpuBigIntDivRem.Div(Value(workspace, a, aWords), Value(workspace, b, bWords), r));
        ExpectExact(workspace, a % b, Math.Min(aWords, bWords),
            r => GpuBigIntDivRem.Mod(Value(workspace, a, aWords), Value(workspace, b, bWords), r));
        ExpectExact(workspace, a % b, Math.Max(aWords, bWords) + 1,
            r => GpuBigIntDivRem.Mod(Value(workspace, a, aWords), Value(workspace, b, bWords), r));
        if (aWords > 1)
        {
            ExpectRejected(workspace, aWords - 1,
                r => GpuBigIntDivRem.Div(Value(workspace, a, aWords), Value(workspace, b, bWords), r));
        }

        // gcd fits the narrower operand
        ExpectExact(workspace, BigInteger.GreatestCommonDivisor(a, b), Math.Min(aWords, bWords),
            r => GpuBigIntMath.Gcd(Value(workspace, a, aWords), Value(workspace, b, bWords), r));

        // The inverse of a value of any width modulo the odd modulus, into a buffer wider than the modulus; the value
        // is made coprime to the modulus on the host
        var coprime = a;
        while (!BigInteger.GreatestCommonDivisor(coprime, b).IsOne)
        {
            coprime++;
        }

        ExpectExact(workspace, Inverse(coprime % b, b), bWords + 1,
            r => GpuBigIntMath.ModInverse(Value(workspace, coprime, aWords), Value(workspace, b, bWords), r));

        // The quotient by a 32-bit divisor into a wider buffer
        ExpectExact(workspace, a / 7, aWords + 2,
            r => GpuBigIntMath.DivideByUInt32(Value(workspace, a, aWords), 7u, r));

        // The square root needs half the operand's bits
        var rootWords = Math.Max(1, (aWords + 1) / 2);
        ExpectExact(workspace, ISqrt(a), rootWords,
            r => GpuBigIntDivRem.Sqrt(Value(workspace, a, aWords), r));
        ExpectExact(workspace, ISqrt(a), aWords + 1,
            r => GpuBigIntDivRem.Sqrt(Value(workspace, a, aWords), r));
    }

    public static void Shifts(GpuWorkspace workspace, int aWords)
    {
        var a = AllOnes(32 * aWords - 3); // the top three bits are clear
        foreach (var k in new[] { 0, 1, 3, 4, 31, 32, 33, 65 })
        {
            var exactWords = (32 * aWords + k + 31) / 32;

            // At the bound: exact, zeros above in a wider buffer
            ExpectExact(workspace, a << k, exactWords + 1,
                r => GpuBigIntMath.ShiftLeft(Value(workspace, a, aWords), k, r));

            // In the operand width: checked; up to 3 bits fit, more is a domain error
            // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntMath.cs ShiftLeftCore without the
            // fit check (the shifted-out bits are dropped silently)
            if (k <= 3)
            {
                ExpectExact(workspace, a << k, aWords,
                    r => GpuBigIntMath.ShiftLeft(Value(workspace, a, aWords), k, r));
            }
            else
            {
                ExpectDomainError(workspace, aWords,
                    r => GpuBigIntMath.ShiftLeft(Value(workspace, a, aWords), k, r));
            }

            // Right: as narrow as the remaining bits
            var rightWords = Math.Max(1, (32 * aWords - k + 31) / 32);
            ExpectExact(workspace, a >> k, rightWords,
                r => GpuBigIntMath.ShiftRight(Value(workspace, a, aWords), k, r));
            ExpectExact(workspace, a >> k, aWords + 1,
                r => GpuBigIntMath.ShiftRight(Value(workspace, a, aWords), k, r));
        }

        if (aWords > 1)
        {
            ExpectRejected(workspace, aWords - 1, r => GpuBigIntMath.ShiftLeft(Value(workspace, 1, aWords), 1, r));
        }
    }

    // The call writes the exact value and zeros above it into a buffer of resultWords words pre-filled with Fill
    private static void ExpectExact(GpuWorkspace workspace, BigInteger expected, int resultWords,
        Action<GpuBigUInt> call)
    {
        using var r = Filled(workspace, resultWords);
        try
        {
            call(r);
            workspace.ThrowIfDeviceErrors();
            Assert.Equal(Limbs(expected, resultWords), r.ToHostArray());
        }
        finally
        {
            ReleaseOperands();
        }
    }

    // The call is a domain error: OverflowException at the call (CPU fallback) or at the next read (GPU)
    private static void ExpectDomainError(GpuWorkspace workspace, int resultWords, Action<GpuBigUInt> call)
    {
        using var r = Filled(workspace, resultWords);
        try
        {
            Assert.Throws<OverflowException>(() =>
            {
                call(r);
                workspace.ThrowIfDeviceErrors();
            });
        }
        finally
        {
            ReleaseOperands();
        }
    }

    // The call is rejected before it writes anything
    private static void ExpectRejected(GpuWorkspace workspace, int resultWords, Action<GpuBigUInt> call)
    {
        if (resultWords < 1)
        {
            return;
        }

        using var r = Filled(workspace, resultWords);
        try
        {
            Assert.Throws<ArgumentException>(() => call(r));
            Assert.All(r.ToHostArray(), static w => Assert.Equal(Fill, w));
        }
        finally
        {
            ReleaseOperands();
        }
    }

    // An operand made inside a call; ReleaseOperands disposes it after the check
    private static GpuBigUInt Value(GpuWorkspace workspace, BigInteger value, int words)
    {
        var operand = new GpuBigUInt(workspace, Limbs(value, words));
        s_operands.Add(operand);
        return operand;
    }

    private static void ReleaseOperands()
    {
        foreach (var operand in s_operands)
        {
            operand.Dispose();
        }

        s_operands.Clear();
    }

    private static GpuBigUInt Filled(GpuWorkspace workspace, int words) =>
        new(workspace, Enumerable.Repeat(Fill, words).ToArray());

    private static uint[] Limbs(BigInteger value, int words)
    {
        var limbs = new uint[words];
        value.WriteLimbs(limbs);
        return limbs;
    }

    private static BigInteger AllOnes(int bits) => (BigInteger.One << bits) - 1;

    // A value whose top word is non-zero, from a fixed seed
    private static BigInteger Random(int words, int seed)
    {
        var random = new Random(1433 * 100 + words * 7 + seed);
        var bytes = new byte[words * 4 + 1];
        random.NextBytes(bytes);
        bytes[^1] = 0;
        bytes[^2] |= 0x80;
        return new BigInteger(bytes);
    }

    private static BigInteger ISqrt(BigInteger n)
    {
        if (n.IsZero)
        {
            return 0;
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

    // The inverse of value modulo modulus by the extended Euclidean algorithm (gcd 1)
    private static BigInteger Inverse(BigInteger value, BigInteger modulus)
    {
        BigInteger r0 = modulus, r1 = value, t0 = 0, t1 = 1;
        while (!r1.IsZero)
        {
            var q = r0 / r1;
            (r0, r1) = (r1, r0 - q * r1);
            (t0, t1) = (t1, t0 - q * t1);
        }

        return t0.Sign < 0 ? t0 + modulus : t0;
    }
}
