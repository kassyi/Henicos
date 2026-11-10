namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of the vector element bounds, against a BigInteger oracle computed from the host inputs:
// - the bound of an element is tracked in bits: a chain of additions grows by one bit per addition, by one word only
//   every 32 additions;
// - Sum of Capacity elements of b bits has b + ceil(log2 Capacity) bits and never overflows;
// - a difference of equal magnitudes is +0.
public partial class GpuBigIntVectorLinqExtensionsTests
{
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ZipAddChain_GrowsOneBitPerAddition()
    {
        // 33 additions of one-word elements: 32 + 33 bits, three words; a word per addition gave 34
        // mutant: VectorBinaryVectorExpressions.cs VectorAddVectorExpression bound
        //   `BigIntWidths.Add(left.BitsPerElement, right.BitsPerElement)` -> `OfWords(max words + 1)`
        using var ones = CreateArray([uint.MaxValue, 1], 1);
        IGpuBigIntVectorExpression e = ones.AsVectorizedEnumerable();
        Assert.Equal(32L, e.BitsPerElement);
        for (var i = 0; i < 33; i++)
        {
            e = e.ZipAdd(ones.AsVectorizedEnumerable());
        }

        Assert.Equal(32L + 33, e.BitsPerElement);
        Assert.Equal(3, e.WordsPerElement);
        var values = ToHostArray(e.Evaluate(_context));
        Assert.Equal([34 * (BigInteger)uint.MaxValue, 34], values);
        e.Dispose();
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(2, 33)]
    [InlineData(1, 1024)]
    [InlineData(1, 1025)]
    public void Sum_OfFullWidthElements_HasTheLogBound_AndNeverOverflows(int numWords, int capacity)
    {
        // Capacity elements of all ones: the sum needs 32 N + ceil(log2 Capacity) bits
        // mutant: VectorReductionExpressions.cs VectorSumReductionExpression.Bits without `+ CeilLog2(...)`
        //   (the finalize kernel reports the bits that do not fit)
        var allOnes = AllOnes(numWords);
        var values = Enumerable.Repeat(allOnes, capacity).ToArray();
        using var array = CreateArray(values, numWords);

        var sum = array.AsVectorizedEnumerable().Sum();
        var log = capacity <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(capacity));
        Assert.Equal(BigIntWidths.Words(32L * numWords + log), sum.Node.NumWords);
        Assert.Equal(allOnes * capacity, EvaluateReduction(sum));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Sum_ThatLeavesTheWidthAndComesBack_IsExact()
    {
        // [2^(32N) - 1, 1, -1]: the running sum needs N + 1 words; the result has the bound of three elements
        var values = new[] { SignedElement.From(AllOnes(2)), 1, -1 };
        using var array = CreateSignedArray(values, 2);

        Assert.Equal(AllOnes(2), EvaluateReduction(array.AsVectorizedEnumerable().Sum()));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void ZipSubtract_OfEqualMagnitudes_IsPositiveZero()
    {
        // -5 - (-5) and 5 - 5: the magnitudes cancel; the sign word is 0 (BigInteger has no negative zero)
        // mutant: GpuBigIntVectorKernels.AddSub.cu.cs `nonZero[0] == 0u ? 0u : ` removed (the left sign is kept)
        using var left = CreateArray([-5, 5, -7], 2);
        using var right = CreateArray([-5, 5, -2], 2);
        using var difference = left.AsVectorizedEnumerable().ZipSubtract(right.AsVectorizedEnumerable());

        var result = difference.Evaluate(_context);
        var signs = new uint[3];
        _context.Provider.CopyDeviceToHost(signs, result.Signs.DevicePointer, 3, _context.Stream);
        Assert.Equal([0u, 0u, 1u], signs);
        Assert.Equal([BigInteger.Zero, 0, -5], ToHostArray(result));
    }
}
