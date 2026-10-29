namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// Full-width products of <c>SelectMultiply</c> and <c>ZipMultiply</c> against <see cref="BigInteger"/> (#883).
/// </summary>
/// <remarks>
/// The result of a vector multiplication is <c>left + right</c> words wide, so every product fits; the property is that
/// it is exact. The widths reach the kernel's boundaries: one word, a warp boundary of the left operand (33 words, the
/// block has more than one warp), and a left operand longer than the 1024 threads of a block (1025 words, two passes of
/// the row loop with the carry handed from the first chunk to the second).
/// </remarks>
public partial class GpuBigIntVectorLinqExtensionsTests
{
    public static TheoryData<int, int, int> FullWidthMultiplyCases() => new()
    {
        { 1, 1, 11 },
        { 2, 2, 12 },
        { 16, 8, 13 },
        { 16, 16, 14 },
        { 33, 1, 15 },
        { 33, 33, 16 },
        { 64, 1, 17 },
        { 64, 64, 18 },
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
