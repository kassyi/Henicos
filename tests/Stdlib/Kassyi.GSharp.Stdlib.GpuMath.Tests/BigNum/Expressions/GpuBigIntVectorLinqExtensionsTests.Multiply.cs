namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// Full-width products of <c>SelectMultiply</c> and <c>ZipMultiply</c> against <see cref="BigInteger"/> (#883).
/// </summary>
/// <remarks>
/// The result of a vector multiplication is <c>left + right</c> words wide, so every product fits; the property is that
/// it is exact. The widths reach the kernel's boundaries: one word, both sides of a warp boundary of the left operand
/// (32 and 33 words; at 33 the block has more than one warp), a left operand that exactly fills a block (1024 words: the
/// top thread is lane 31 of the last warp and thread <c>bDim - 1</c> at once), and a left operand longer than the 1024
/// threads of a block (1025 words, two passes of the row loop with the carry handed from the first chunk to the second).
/// </remarks>
public partial class GpuBigIntVectorLinqExtensionsTests
{
    public static TheoryData<int, int, int> FullWidthMultiplyCases() => new()
    {
        { 1, 1, 11 },
        { 2, 2, 12 },
        { 16, 8, 13 },
        { 16, 16, 14 },
        { 32, 1, 20 },
        { 33, 1, 15 },
        { 33, 33, 16 },
        { 64, 1, 17 },
        { 64, 64, 18 },
        { 1024, 1, 21 },
        { 1025, 2, 19 }
    };

    [SkippableTheory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(FullWidthMultiplyCases))]
    public void SelectMultiply_FullWidthOperands_IsExact(int leftWords, int rightWords, int seed)
    {
        var random = new Random(seed);
        var left = RandomFullWidthElements(random, 3, leftWords);
        var scalarValue = RandomFullWidth(random, rightWords, negative: true);

        using var array = CreateArray(left, leftWords);
        using var scalar = CreateScalar(scalarValue, rightWords);
        using var expr = array.AsVectorizedEnumerable().SelectMultiply(scalar);
        var actual = ToHostArray(expr.Evaluate(_context));

        // Each element is the exact product: the carry out of the left operand's top word reaches word i + leftWords.
        // mutant: GpuBigIntVectorKernels.Mul.cu.cs, the low pass's carry out `= g | (p & carryIn)` -> `= 0u`
        // input: every case with rightWords >= 2 (row 0 adds to zeros and never carries out)
        // mutant: the chunk carry `PropagateBlockCarry(g, p, blockCarryIn, ...)` -> `PropagateBlockCarry(g, p, 0u, ...)`
        // input: { 1025, 2 } (the left operand spans two chunks of 1024 threads)
        for (int e = 0; e < left.Length; e++)
        {
            Assert.True(left[e] * scalarValue == actual[e], $"seed {seed}, element {e}");
        }
    }

    [SkippableTheory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(FullWidthMultiplyCases))]
    public void ZipMultiply_FullWidthOperands_IsExact(int leftWords, int rightWords, int seed)
    {
        var random = new Random(seed);
        var left = RandomFullWidthElements(random, 3, leftWords);
        var right = RandomFullWidthElements(random, 3, rightWords);

        using var leftArray = CreateArray(left, leftWords);
        using var rightArray = CreateArray(right, rightWords);
        using var expr = leftArray.AsVectorizedEnumerable().ZipMultiply(rightArray.AsVectorizedEnumerable());
        using var swapped = rightArray.AsVectorizedEnumerable().ZipMultiply(leftArray.AsVectorizedEnumerable());
        var actual = ToHostArray(expr.Evaluate(_context));
        var actualSwapped = ToHostArray(swapped.Evaluate(_context));

        for (int e = 0; e < left.Length; e++)
        {
            // Each element is the exact product.
            // mutant: GpuBigIntVectorKernels.Mul.cu.cs, `valid = j < leftWords && ...` -> `valid = j < leftWords - 1 && ...`
            // input: every case
            Assert.True(left[e] * right[e] == actual[e], $"seed {seed}, element {e}");

            // Swapping the operands swaps the roles of the row loop (right words) and the threads (left words), so a
            // carry lost at either end shows up as a difference without the reference.
            // mutant: the same `j < leftWords - 1`, with the reference check above disabled
            // input: every case except { 1, 1 } (symmetric)
            Assert.True(actual[e] == actualSwapped[e], $"seed {seed}, element {e}, swapped");
        }
    }

    public static TheoryData<int, int> MaxValueMultiplyCases() => new()
    {
        { 1, 1 },
        { 2, 3 },
        { 16, 8 },
        { 33, 33 },
        { 1025, 2 }
    };

    [SkippableTheory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(MaxValueMultiplyCases))]
    public void ZipMultiply_MaxValueOperands_FillsTheResultExactly(int leftWords, int rightWords)
    {
        // (2^32L - 1)(2^32R - 1) = 2^32(L+R) - 2^32L - 2^32R + 1: every word of both operands is 0xFFFFFFFF, so every
        // partial sum carries, and the product needs all L + R words of the result.
        var left = (BigInteger.One << (32 * leftWords)) - 1;
        var right = (BigInteger.One << (32 * rightWords)) - 1;
        var expected = left * right;
        Assert.True(expected.GetBitLength() > 32L * (leftWords + rightWords - 1), "the product must use the top word");

        using var leftArray = CreateArray([left, -left], leftWords);
        using var rightArray = CreateArray([right, right], rightWords);
        using var expr = leftArray.AsVectorizedEnumerable().ZipMultiply(rightArray.AsVectorizedEnumerable());
        var actual = ToHostArray(expr.Evaluate(_context));

        // The product of the largest operands is exact, with the sign of the left operand.
        // mutant: GpuBigIntVectorKernels.Mul.cu.cs, the carry-in dropped (`(uint)sum + carryIn` -> `(uint)sum`)
        // input: every case except { 1, 1 } (a single product never carries between words)
        Assert.Equal(expected, actual[0]);
        Assert.Equal(-expected, actual[1]);
    }

    [SkippableTheory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(4)]
    [InlineData(34)]
    [InlineData(1026)]
    public void ZipMultiply_PropagateChain_IsExact(int leftWords)
    {
        // left = [0xFFFFFFFF, 1, ..., 1, 0xFFFFFFFF] (low word first) times 0xFFFFFFFF: the low pass leaves words
        // 1 .. leftWords - 2 at 0xFFFFFFFF and word leftWords - 1 at 1. The high pass adds 0xFFFFFFFE to word 1, which
        // carries, and 0 to words 2 .. leftWords - 2, which propagate the carry (propagate, not generate); thread
        // leftWords - 2 adds 0 to word leftWords - 1 = 1, which ends the run. So the run reaches thread leftWords - 2:
        // for 34 words thread 32 (across the warp boundary), for 1026 words thread 1024 (across the chunk boundary).
        var leftValue = PropagateChainOperand(leftWords);
        var rightValue = new BigInteger(uint.MaxValue);

        using var leftArray = CreateArray([leftValue, -leftValue], leftWords);
        using var rightArray = CreateArray([rightValue, rightValue], 1);
        using var expr = leftArray.AsVectorizedEnumerable().ZipMultiply(rightArray.AsVectorizedEnumerable());
        var actual = ToHostArray(expr.Evaluate(_context));

        // The product is exact; the carry crosses the run of 0xFFFFFFFF words.
        // mutant: GpuBigIntVectorKernels.Mul.cu.cs, `p = (uint)sum == 0xFFFFFFFFu ? 1u : 0u` -> `p = 0u`
        // input: every case (the random full-width cases do not reach a propagate chain)
        // mutant: GpuBigIntVectorKernels.cu.cs PropagateBlockCarry, `if (warpId > 0)` -> `if (warpId > 1000)` (no carry
        // between warps)
        // input: 34 and 1026 (33 words would end the run at thread 31, inside warp 0)
        // mutant: GpuBigIntVectorKernels.Mul.cu.cs, `PropagateBlockCarry(g, p, blockCarryIn, ...)` ->
        // `PropagateBlockCarry(g, p, 0u, ...)` (no carry between chunks)
        // input: 1026 (1025 words would end the run at thread 1023, inside chunk 0)
        Assert.Equal(leftValue * rightValue, actual[0]);
        Assert.Equal(-(leftValue * rightValue), actual[1]);
    }

    [SkippableTheory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(4, 4)]
    [InlineData(33, 2)]
    [InlineData(1025, 2)]
    public void FloatZipMultiply_FullWidthSignificands_IsExact(int leftWords, int rightWords)
    {
        // The float vector product shares DeviceVectorMulVectorParallel: its significand is the exact integer product
        // of the operand significands (leftWords + rightWords words) and its exponent is the sum of the exponents.
        var random = new Random(2883 + leftWords);
        var left = RandomFullWidthElements(random, 3, leftWords);
        left[1] = -PropagateChainOperand(leftWords);
        var right = RandomFullWidthElements(random, 3, rightWords);
        right[2] = (BigInteger.One << (32 * rightWords)) - 1;

        using var leftArray = new GpuBigFloatArray(_context, left.Length, leftWords);
        using var rightArray = new GpuBigFloatArray(_context, right.Length, rightWords);
        for (int e = 0; e < left.Length; e++)
        {
            using var leftFloat = CreateFloat(left[e], leftWords, exponent: 3);
            using var rightFloat = CreateFloat(right[e], rightWords, exponent: -5);
            leftArray.SetElement(e, leftFloat);
            rightArray.SetElement(e, rightFloat);
        }

        using var expr = leftArray.AsVectorizedEnumerable().ZipMultiply(rightArray.AsVectorizedEnumerable());
        var result = expr.Evaluate(_context);
        Assert.Equal(leftWords + rightWords, result.WordsPerElement);

        for (int e = 0; e < left.Length; e++)
        {
            var element = result.GetElement(e);
            var magnitude = element.Significand.ToBigInteger();
            var actual = element.IsNegative ? -magnitude : magnitude;

            // The significand is the exact product (main truncated it like the integer product, #883).
            // mutant: GpuBigIntVectorKernels.Mul.cu.cs, the low pass's carry out `= g | (p & carryIn)` -> `= 0u`
            // input: every case (rightWords >= 2)
            Assert.True(left[e] * right[e] == actual, $"seed {2883 + leftWords}, element {e}");
            Assert.Equal(-2, element.Exponent);
        }
    }

    [SkippableFact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void SelectMultiply_TargetOneWordShort_IsRejected()
    {
        // A product needs left + right words. A target one word narrower would lose the top word, so it is rejected
        // before anything is written instead of receiving a truncated value.
        // mutant: VectorResultCache.ThrowIfWrongShape, the width comparison removed
        using var array = CreateArray([BigInteger.One], 2);
        using var scalar = CreateScalar(BigInteger.One, 2);
        using var expr = array.AsVectorizedEnumerable().SelectMultiply(scalar);
        using var shortTarget = new GpuBigUIntArray(_context, 1, expr.WordsPerElement - 1);

        Assert.Equal(4, expr.WordsPerElement);
        Assert.Throws<ArgumentException>(() => expr.Evaluate(_context, targetResult: shortTarget));
    }

    /// <summary>The words <c>[0xFFFFFFFF, 1, ..., 1, 0xFFFFFFFF]</c>, low word first.</summary>
    private static BigInteger PropagateChainOperand(int numWords)
    {
        var words = new uint[numWords];
        for (int i = 0; i < numWords; i++)
        {
            words[i] = i == 0 || i == numWords - 1 ? uint.MaxValue : 1u;
        }

        var bytes = new byte[numWords * 4];
        Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    private GpuBigFloat CreateFloat(BigInteger value, int numWords, int exponent)
    {
        bool isNegative = value < 0;
        var bytes = BigInteger.Abs(value).ToByteArray(isUnsigned: true, isBigEndian: false);
        var words = new uint[numWords];
        Buffer.BlockCopy(bytes, 0, words, 0, Math.Min(bytes.Length, words.Length * 4));
        return new GpuBigFloat(new GpuBigUInt(_context, words), exponent, isNegative);
    }

    /// <summary>A value of exactly <paramref name="numWords"/> words: random words with the top bit set.</summary>
    private static BigInteger RandomFullWidth(Random random, int numWords, bool negative)
    {
        var bytes = new byte[numWords * 4];
        random.NextBytes(bytes);
        bytes[^1] |= 0x80;
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
        return negative ? -value : value;
    }

    private static BigInteger[] RandomFullWidthElements(Random random, int count, int numWords)
    {
        var values = new BigInteger[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = RandomFullWidth(random, numWords, negative: random.Next(2) == 1);
        }

        return values;
    }
}
