
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

public partial class GpuBigIntVectorLinqExtensionsTests
{
    private uint[] ToHostBoolArray(IGpuBoolVectorExpression expr)
    {
        var array = expr.Evaluate(_context);
        var hostResult = new uint[array.Length];
        _context.Provider.CopyDeviceToHost(hostResult, array.DevicePointer, array.Length);
        array.Dispose();
        return hostResult;
    }

    public static IEnumerable<object[]> BooleanComparisonData()
    {
        yield return new object[] { TestScenario.HappyPath, new BigInteger[] { 10, -5, 100, 50 }, new BigInteger(50), 
            new uint[] { 0, 0, 0, 1 }, // EQ
            new uint[] { 1, 1, 1, 0 }, // NEQ
            new uint[] { 0, 0, 1, 0 }, // GT
            new uint[] { 0, 0, 1, 1 }, // GTE
            new uint[] { 1, 1, 0, 0 }, // LT
            new uint[] { 1, 1, 0, 1 }  // LTE
        };

        yield return new object[] { TestScenario.Boundary, new BigInteger[] { 0, 0, 0, 0 }, new BigInteger(0), 
            new uint[] { 1, 1, 1, 1 }, // EQ
            new uint[] { 0, 0, 0, 0 }, // NEQ
            new uint[] { 0, 0, 0, 0 }, // GT
            new uint[] { 1, 1, 1, 1 }, // GTE
            new uint[] { 0, 0, 0, 0 }, // LT
            new uint[] { 1, 1, 1, 1 }  // LTE
        };
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(BooleanComparisonData))]
    public void IsEqualTo_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, uint[] expectedEq, uint[] expectedNeq, uint[] expectedGt, uint[] expectedGte, uint[] expectedLt, uint[] expectedLte)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().IsEqualTo(scalar);
        var result = ToHostBoolArray(expr);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedEq[i], result[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(BooleanComparisonData))]
    public void IsNotEqualTo_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, uint[] expectedEq, uint[] expectedNeq, uint[] expectedGt, uint[] expectedGte, uint[] expectedLt, uint[] expectedLte)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().IsNotEqualTo(scalar);
        var result = ToHostBoolArray(expr);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedNeq[i], result[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(BooleanComparisonData))]
    public void IsGreaterThan_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, uint[] expectedEq, uint[] expectedNeq, uint[] expectedGt, uint[] expectedGte, uint[] expectedLt, uint[] expectedLte)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().IsGreaterThan(scalar);
        var result = ToHostBoolArray(expr);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedGt[i], result[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(BooleanComparisonData))]
    public void IsGreaterThanOrEqualTo_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, uint[] expectedEq, uint[] expectedNeq, uint[] expectedGt, uint[] expectedGte, uint[] expectedLt, uint[] expectedLte)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().IsGreaterThanOrEqualTo(scalar);
        var result = ToHostBoolArray(expr);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedGte[i], result[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(BooleanComparisonData))]
    public void IsLessThan_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, uint[] expectedEq, uint[] expectedNeq, uint[] expectedGt, uint[] expectedGte, uint[] expectedLt, uint[] expectedLte)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().IsLessThan(scalar);
        var result = ToHostBoolArray(expr);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedLt[i], result[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(BooleanComparisonData))]
    public void IsLessThanOrEqualTo_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, uint[] expectedEq, uint[] expectedNeq, uint[] expectedGt, uint[] expectedGte, uint[] expectedLt, uint[] expectedLte)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().IsLessThanOrEqualTo(scalar);
        var result = ToHostBoolArray(expr);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedLte[i], result[i]);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void LogicalAnd_EvaluatesCorrectly()
    {
        using var array = CreateArray(new BigInteger[] { 10, 50, 100, 200 });
        var lower = CreateScalar(20);
        var upper = CreateScalar(150);
        var expr = array.AsVectorizedEnumerable().IsGreaterThan(lower).And(array.AsVectorizedEnumerable().IsLessThan(upper));
        var result = ToHostBoolArray(expr);
        Assert.Equal(new uint[] { 0, 1, 1, 0 }, result);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void LogicalOr_EvaluatesCorrectly()
    {
        using var array = CreateArray(new BigInteger[] { 10, 50, 100, 200 });
        var lower = CreateScalar(20);
        var upper = CreateScalar(150);
        var expr = array.AsVectorizedEnumerable().IsLessThan(lower).Or(array.AsVectorizedEnumerable().IsGreaterThan(upper));
        var result = ToHostBoolArray(expr);
        Assert.Equal(new uint[] { 1, 0, 0, 1 }, result);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void LogicalNot_EvaluatesCorrectly()
    {
        using var array = CreateArray(new BigInteger[] { 10, 50, 100, 200 });
        var threshold = CreateScalar(60);
        var expr = array.AsVectorizedEnumerable().IsGreaterThan(threshold).Not();
        var result = ToHostBoolArray(expr);
        Assert.Equal(new uint[] { 1, 1, 0, 0 }, result); // Effectively LTE
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void BooleanAny_EvaluatesCorrectly()
    {
        using var array = CreateArray(new BigInteger[] { 10, 50, 100, 200 });
        var threshold = CreateScalar(150);
        var expr = array.AsVectorizedEnumerable().IsGreaterThan(threshold).Any();
        Assert.True(expr.Evaluate(_context)); // 200 > 150
        
        var threshold2 = CreateScalar(300);
        var expr2 = array.AsVectorizedEnumerable().IsGreaterThan(threshold2).Any();
        Assert.False(expr2.Evaluate(_context));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void BooleanAll_EvaluatesCorrectly()
    {
        using var array = CreateArray(new BigInteger[] { 10, 50, 100, 200 });
        var threshold = CreateScalar(0);
        var expr = array.AsVectorizedEnumerable().IsGreaterThan(threshold).All();
        Assert.True(expr.Evaluate(_context)); // All > 0
        
        var threshold2 = CreateScalar(20);
        var expr2 = array.AsVectorizedEnumerable().IsGreaterThan(threshold2).All();
        Assert.False(expr2.Evaluate(_context)); // 10 is not > 20
    }

    /// <summary>
    /// A signed element as stored on the device: a magnitude and a sign word. A negative zero (sign 1, magnitude 0)
    /// cannot be written as a <see cref="BigInteger"/>, so the tests keep the sign separately.
    /// </summary>
    public readonly record struct SignedElement(BigInteger Magnitude, bool Negative)
    {
        public BigInteger Value => Negative ? -Magnitude : Magnitude;

        public static implicit operator SignedElement(long value) => From(value);

        public static SignedElement From(BigInteger value) => new(BigInteger.Abs(value), value.Sign < 0);
    }

    private static readonly SignedElement s_negativeZero = new(BigInteger.Zero, Negative: true);

    private static readonly BigInteger s_topBit256 = BigInteger.One << 255;

    private static uint[] ToWords(BigInteger magnitude, int numWords)
    {
        var bytes = magnitude.ToByteArray(isUnsigned: true, isBigEndian: false);
        var words = new uint[numWords];
        Buffer.BlockCopy(bytes, 0, words, 0, Math.Min(bytes.Length, numWords * 4));
        return words;
    }

    private GpuBigIntArray CreateSignedArray(SignedElement[] values, int numWords)
    {
        var array = new GpuBigIntArray(_context, values.Length, numWords);
        var valuesHost = new uint[values.Length * numWords];
        var signsHost = new uint[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            Array.Copy(ToWords(values[i].Magnitude, numWords), 0, valuesHost, i * numWords, numWords);
            signsHost[i] = values[i].Negative ? 1u : 0u;
        }

        _context.Provider.CopyHostToDevice(array.Values.DevicePointer, valuesHost, valuesHost.Length);
        _context.Provider.CopyHostToDevice(array.Signs.DevicePointer, signsHost, signsHost.Length);
        return array;
    }

    private static IGpuBoolVectorExpression Compare(
        IGpuBigIntVectorExpression left,
        IGpuBigIntVectorExpression right,
        VectorCompareOperator op) => op switch
    {
        VectorCompareOperator.Equal => left.IsEqualTo(right),
        VectorCompareOperator.NotEqual => left.IsNotEqualTo(right),
        VectorCompareOperator.GreaterThan => left.IsGreaterThan(right),
        VectorCompareOperator.GreaterThanOrEqual => left.IsGreaterThanOrEqualTo(right),
        VectorCompareOperator.LessThan => left.IsLessThan(right),
        _ => left.IsLessThanOrEqualTo(right),
    };

    private static IGpuBoolVectorExpression Compare(
        IGpuBigIntVectorExpression left,
        GpuBigInt right,
        VectorCompareOperator op) => op switch
    {
        VectorCompareOperator.Equal => left.IsEqualTo(right),
        VectorCompareOperator.NotEqual => left.IsNotEqualTo(right),
        VectorCompareOperator.GreaterThan => left.IsGreaterThan(right),
        VectorCompareOperator.GreaterThanOrEqual => left.IsGreaterThanOrEqualTo(right),
        VectorCompareOperator.LessThan => left.IsLessThan(right),
        _ => left.IsLessThanOrEqualTo(right),
    };

    // The reference: the operator applied to BigInteger.CompareTo of the values the test holds (BigInteger has no -0).
    private static uint ExpectedCompare(BigInteger left, BigInteger right, VectorCompareOperator op)
    {
        var cmp = left.CompareTo(right);
        var holds = op switch
        {
            VectorCompareOperator.Equal => cmp == 0,
            VectorCompareOperator.NotEqual => cmp != 0,
            VectorCompareOperator.GreaterThan => cmp > 0,
            VectorCompareOperator.GreaterThanOrEqual => cmp >= 0,
            VectorCompareOperator.LessThan => cmp < 0,
            _ => cmp <= 0,
        };
        return holds ? 1u : 0u;
    }

    private uint[] EvaluateBools(IGpuBoolVectorExpression expr)
    {
        using (expr)
        {
            var array = expr.Evaluate(_context);
            var host = new uint[array.Length];
            if (host.Length > 0)
            {
                _context.Provider.CopyDeviceToHost(host, array.DevicePointer, array.Length);
            }

            return host;
        }
    }

    private uint[] CompareVectors(SignedElement[] left, int leftWords, SignedElement[] right, int rightWords, VectorCompareOperator op)
    {
        using var leftArray = CreateSignedArray(left, leftWords);
        using var rightArray = CreateSignedArray(right, rightWords);
        return EvaluateBools(Compare(leftArray.AsVectorizedEnumerable(), rightArray.AsVectorizedEnumerable(), op));
    }

    private static uint[] ExpectedCompareVectors(SignedElement[] left, SignedElement[] right, VectorCompareOperator op) =>
        left.Zip(right, (l, r) => ExpectedCompare(l.Value, r.Value, op)).ToArray();

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(VectorCompareOperator.Equal)]
    [InlineData(VectorCompareOperator.NotEqual)]
    [InlineData(VectorCompareOperator.GreaterThan)]
    [InlineData(VectorCompareOperator.GreaterThanOrEqual)]
    [InlineData(VectorCompareOperator.LessThan)]
    [InlineData(VectorCompareOperator.LessThanOrEqual)]
    public void CompareVector_SignedElements_MatchesBigIntegerCompareTo(VectorCompareOperator op)
    {
        // Each column is one (left, right) pair:
        // - (-100, 50), (50, -100): the signs differ and the negative side has the larger magnitude
        // - (-3, 3), (3, -3): equal magnitudes, different signs
        // - (-3, -100), (-100, -3): both negative, so the order is the reverse of the magnitude order
        // - (-5, -5): both negative and equal
        // - (-0, 0), (0, -0), (-0, -0): a negative zero equals zero
        // - (5, 5), (0, 0): equal
        // - (2^255, 2^255 + 1), (2^255 + 1, 2^255), (-2^255, 2^255): only the lowest word differs while the top bit
        //   of 8 words is set; the top bit is part of the magnitude, not a sign
        // mutant: GpuBigIntCmpKernels.cu.cs DecideSignedCompare `return sa ? (3u - magCmp) : magCmp` -> `return magCmp`
        // input: (-3, -100)
        // mutant: GpuBigIntCmpKernels.cu.cs DecideSignedCompare `bool sa = signA != 0u && nonzeroA` -> `bool sa = signA != 0u`
        // input: (-0, 0) with Equal
        // mutant: GpuBigIntCmpKernels.cu.cs DecideSignedCompare `return sa ? 1u : 2u` -> `return sa ? 2u : 1u`
        // input: (-100, 50)
        SignedElement[] left = [-100, 50, -3, 3, -3, -100, -5, s_negativeZero, 0, s_negativeZero, 5, 0,
            SignedElement.From(s_topBit256), SignedElement.From(s_topBit256 + 1), SignedElement.From(-s_topBit256)];
        SignedElement[] right = [50, -100, 3, -3, -100, -3, -5, 0, s_negativeZero, s_negativeZero, 5, 0,
            SignedElement.From(s_topBit256 + 1), SignedElement.From(s_topBit256), SignedElement.From(s_topBit256)];

        var actual = CompareVectors(left, 8, right, 8, op);

        Assert.Equal(ExpectedCompareVectors(left, right, op), actual);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(VectorCompareOperator.Equal)]
    [InlineData(VectorCompareOperator.NotEqual)]
    [InlineData(VectorCompareOperator.GreaterThan)]
    [InlineData(VectorCompareOperator.GreaterThanOrEqual)]
    [InlineData(VectorCompareOperator.LessThan)]
    [InlineData(VectorCompareOperator.LessThanOrEqual)]
    public void CompareVector_DifferentWordCounts_MatchesBigIntegerCompareTo(VectorCompareOperator op)
    {
        // A 4-word vector against an 8-word vector, in both directions. Each side is read with its own stride, and the
        // missing upper words of the narrower side are 0. (2^127, 2^200) differs only in a word that the 4-word side
        // does not have.
        // mutant: VectorCompareExpressions.cs `Right = VectorCompareLaunch.Operand(rightResult),` -> `... with { Stride = leftResult.WordsPerElement },`
        // input: [1, 2, 3] (4 words) against [1, 2, 3] (8 words)
        // mutant: GpuBigIntCmpKernels.cu.cs ReadCompareWord `idx < operand.NumWords ? ... : 0u` -> `operand.Limbs[...]`
        // input: 2^127 (4 words) against 2^200 (8 words)
        var big127 = BigInteger.One << 127;
        var big200 = BigInteger.One << 200;
        SignedElement[] narrow = [1, 2, 3, SignedElement.From(big127), SignedElement.From(big127), -5, SignedElement.From(-big127)];
        SignedElement[] wide = [1, 2, 3, SignedElement.From(big200), SignedElement.From(big127), 5, SignedElement.From(-big200)];

        var narrowLeft = CompareVectors(narrow, 4, wide, 8, op);
        var wideLeft = CompareVectors(wide, 8, narrow, 4, op);

        Assert.Equal(ExpectedCompareVectors(narrow, wide, op), narrowLeft);
        Assert.Equal(ExpectedCompareVectors(wide, narrow, op), wideLeft);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(VectorCompareOperator.Equal)]
    [InlineData(VectorCompareOperator.NotEqual)]
    [InlineData(VectorCompareOperator.GreaterThan)]
    [InlineData(VectorCompareOperator.GreaterThanOrEqual)]
    [InlineData(VectorCompareOperator.LessThan)]
    [InlineData(VectorCompareOperator.LessThanOrEqual)]
    public void CompareVector_WiderThanOneBlock_MatchesBigIntegerCompareTo(VectorCompareOperator op)
    {
        // 2048 words: the block has 1024 threads, so each thread reads two words, and the non-zero test is combined
        // across all 32 warps.
        // - (-1, 0): the only non-zero word (0) is read in the first round; a thread must keep it through the second
        // - (-2^(32*40), 0): the only non-zero word (40) belongs to thread 40, in warp 1
        // - (2^(32*1030) + 1, 2): the most significant difference is in the second round
        // mutant: GpuBigIntCmpKernels.cu.cs `myNonzeroA = myNonzeroA || wa != 0u` -> `myNonzeroA = wa != 0u`
        // input: (-1, 0)
        // mutant: GpuBigIntCmpKernels.cu.cs `for (int w = 0; w < numWarps; w++)` -> `for (int w = 0; w < 1; w++)`
        // input: (-2^(32*40), 0)
        SignedElement[] left = [-1, SignedElement.From(-(BigInteger.One << (32 * 40))),
            SignedElement.From((BigInteger.One << (32 * 1030)) + 1)];
        SignedElement[] right = [0, 0, 2];

        var actual = CompareVectors(left, 2048, right, 2048, op);

        Assert.Equal(ExpectedCompareVectors(left, right, op), actual);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Compare_EmptyVectors_ReturnEmptyResult()
    {
        // A grid of 0 blocks is an invalid launch; an empty comparison launches nothing and returns 0 flags.
        // mutant: VectorCompareExpressions.cs VectorCompareLaunch.Launch `if (length == 0) { return; }` -> removed
        // input: this test (CudaException: invalid value)
        using var left = new GpuBigIntArray(_context, 0, 8);
        using var right = new GpuBigIntArray(_context, 0, 4);
        using var scalarMagnitude = new GpuBigUInt(_context, [5u]);
        var scalar = new GpuBigInt(scalarMagnitude, isNegative: false);

        Assert.Empty(EvaluateBools(left.AsVectorizedEnumerable().IsLessThan(right.AsVectorizedEnumerable())));
        Assert.Empty(EvaluateBools(left.AsVectorizedEnumerable().IsEqualTo(scalar)));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void CompareVector_DifferentLengths_ThrowsArgumentException()
    {
        using var shorter = CreateSignedArray([1, 2], 8);
        using var longer = CreateSignedArray([1, 2, 3], 8);

        Assert.Throws<ArgumentException>(() =>
            EvaluateBools(shorter.AsVectorizedEnumerable().IsEqualTo(longer.AsVectorizedEnumerable())));
        Assert.Throws<ArgumentException>(() =>
            EvaluateBools(longer.AsVectorizedEnumerable().IsLessThan(shorter.AsVectorizedEnumerable())));
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(VectorCompareOperator.Equal)]
    [InlineData(VectorCompareOperator.NotEqual)]
    [InlineData(VectorCompareOperator.GreaterThan)]
    [InlineData(VectorCompareOperator.GreaterThanOrEqual)]
    [InlineData(VectorCompareOperator.LessThan)]
    [InlineData(VectorCompareOperator.LessThanOrEqual)]
    public void CompareScalar_SignedElements_MatchesBigIntegerCompareTo(VectorCompareOperator op)
    {
        // Every element against every scalar, including -0 on either side and values whose top bit of 8 words is set.
        // mutant: VectorCompareExpressions.cs VectorCompareScalarExpression `ScalarSign = scalarResult.IsNegative ? 1u : 0u` -> `ScalarSign = 0u`
        // input: element -100 against scalar -3
        SignedElement[] elements = [-100, -3, s_negativeZero, 0, 3, 50, 100,
            SignedElement.From(s_topBit256), SignedElement.From(-s_topBit256)];
        SignedElement[] scalars = [50, 3, -3, -100, 0, s_negativeZero, SignedElement.From(s_topBit256)];

        using var array = CreateSignedArray(elements, 8);
        foreach (var scalarValue in scalars)
        {
            using var scalarMagnitude = new GpuBigUInt(_context, ToWords(scalarValue.Magnitude, 8));
            var scalar = new GpuBigInt(scalarMagnitude, scalarValue.Negative);

            var actual = EvaluateBools(Compare(array.AsVectorizedEnumerable(), scalar, op));

            var expected = elements.Select(e => ExpectedCompare(e.Value, scalarValue.Value, op)).ToArray();
            Assert.True(expected.SequenceEqual(actual),
                $"scalar {scalarValue}: expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}]");
        }
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(VectorCompareOperator.Equal)]
    [InlineData(VectorCompareOperator.NotEqual)]
    [InlineData(VectorCompareOperator.GreaterThan)]
    [InlineData(VectorCompareOperator.GreaterThanOrEqual)]
    [InlineData(VectorCompareOperator.LessThan)]
    [InlineData(VectorCompareOperator.LessThanOrEqual)]
    public void CompareScalar_DifferentWordCounts_MatchesBigIntegerCompareTo(VectorCompareOperator op)
    {
        // A 4-word vector against an 8-word scalar whose value needs the upper words, and an 8-word vector against a
        // 1-word scalar.
        // mutant: VectorCompareExpressions.cs VectorCompareScalarExpression `NumWords = scalarResult.Value.NumWords` -> `NumWords = vecResult.WordsPerElement`
        // input: the 4-word vector against the 8-word scalar 2^200 (read with 4 words, the scalar looks like 0)
        // mutant: GpuBigIntCmpKernels.cu.cs DeviceVectorCompareSignedParallel `int numWords = ctx.Left.NumWords > ctx.Right.NumWords ? ... : ...` -> `int numWords = ctx.Left.NumWords`
        // input: the 4-word vector against the 2048-word scalar 2^(32*1500). The block has 1024 threads, so the loop
        // over the words decides what is read; with the 8-word scalar the block alone covers every word and the
        // mutant survives.
        SignedElement[] elements = [1, -1, SignedElement.From(BigInteger.One << 127)];
        var big200 = BigInteger.One << 200;

        using var narrowArray = CreateSignedArray(elements, 4);
        using var wideScalarMagnitude = new GpuBigUInt(_context, ToWords(big200, 8));
        foreach (var negative in new[] { false, true })
        {
            var wideScalar = new GpuBigInt(wideScalarMagnitude, negative);
            var actual = EvaluateBools(Compare(narrowArray.AsVectorizedEnumerable(), wideScalar, op));
            var scalarValue = negative ? -big200 : big200;
            Assert.Equal(elements.Select(e => ExpectedCompare(e.Value, scalarValue, op)).ToArray(), actual);
        }

        var big1500 = BigInteger.One << (32 * 1500);
        using var hugeScalarMagnitude = new GpuBigUInt(_context, ToWords(big1500, 2048));
        var hugeScalar = new GpuBigInt(hugeScalarMagnitude, isNegative: false);
        var hugeActual = EvaluateBools(Compare(narrowArray.AsVectorizedEnumerable(), hugeScalar, op));
        Assert.Equal(elements.Select(e => ExpectedCompare(e.Value, big1500, op)).ToArray(), hugeActual);

        using var wideArray = CreateSignedArray(elements, 8);
        using var narrowScalarMagnitude = new GpuBigUInt(_context, [1u]);
        var narrowScalar = new GpuBigInt(narrowScalarMagnitude, isNegative: true);
        var wideActual = EvaluateBools(Compare(wideArray.AsVectorizedEnumerable(), narrowScalar, op));
        Assert.Equal(elements.Select(e => ExpectedCompare(e.Value, BigInteger.MinusOne, op)).ToArray(), wideActual);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(VectorCompareOperator.Equal)]
    [InlineData(VectorCompareOperator.NotEqual)]
    [InlineData(VectorCompareOperator.GreaterThan)]
    [InlineData(VectorCompareOperator.GreaterThanOrEqual)]
    [InlineData(VectorCompareOperator.LessThan)]
    [InlineData(VectorCompareOperator.LessThanOrEqual)]
    public void CompareScalar_ScalarNarrowerThanVector_ReadsOnlyScalarWords(VectorCompareOperator op)
    {
        // The scalar is element 0 of a 1-word array [5, 0xFFFFFFFF], so the word right after it is not 0. A kernel that
        // reads the scalar with the vector's 64 words sees 0xFFFFFFFF in word 1 and finds 5 != 5.
        // mutant: VectorCompareExpressions.cs VectorCompareScalarExpression `NumWords = scalarResult.Value.NumWords` -> `NumWords = vecResult.WordsPerElement`
        // input: this test with Equal
        SignedElement[] elements = [5, 6];
        using var array = CreateSignedArray(elements, 64);
        using var backing = new GpuBigUIntArray(_context, 2, 1);
        _context.Provider.CopyHostToDevice(backing.DevicePointer, [5u, 0xFFFFFFFFu], 2);
        var scalar = new GpuBigInt(backing.BorrowElement(0), isNegative: false);

        var actual = EvaluateBools(Compare(array.AsVectorizedEnumerable(), scalar, op));

        Assert.Equal(elements.Select(e => ExpectedCompare(e.Value, 5, op)).ToArray(), actual);
    }
}
