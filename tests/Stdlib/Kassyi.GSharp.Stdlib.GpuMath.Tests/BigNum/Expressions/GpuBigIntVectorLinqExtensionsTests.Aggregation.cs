
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

public partial class GpuBigIntVectorLinqExtensionsTests
{
    public static IEnumerable<object[]> AggregationData()
    {
        // Happy Path
        yield return new object[] { TestScenario.HappyPath, new BigInteger[] { 10, -5, 15, 100 }, new BigInteger(120), new BigInteger(100), new BigInteger(-5), 4 };
        // Boundary (Zeros)
        yield return new object[] { TestScenario.Boundary, new BigInteger[] { 0, 0, 0, 0 }, new BigInteger(0), new BigInteger(0), new BigInteger(0), 4 };
        // Edge (Large Grid, mixed signs)
        var largeArray = new BigInteger[2048];
        BigInteger sum = 0;
        BigInteger max = -10000;
        BigInteger min = 10000;
        for (int i = 0; i < 2048; i++)
        {
            largeArray[i] = (i % 2 == 0) ? i : -i;
            sum += largeArray[i];
            if (largeArray[i] > max) max = largeArray[i];
            if (largeArray[i] < min) min = largeArray[i];
        }
        yield return new object[] { TestScenario.Boundary, largeArray, sum, max, min, 2048 };
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(AggregationData))]
    public void Sum_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger expectedSum, BigInteger expectedMax, BigInteger expectedMin, int expectedCount)
    {
        using var array = CreateArray(input);
        var sumExpr = array.AsVectorizedEnumerable().Sum();
        using var result = sumExpr.Evaluate(_context);
        Assert.Equal(expectedSum, ToBigInt(result));
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(AggregationData))]
    public void Max_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger expectedSum, BigInteger expectedMax, BigInteger expectedMin, int expectedCount)
    {
        using var array = CreateArray(input);
        var maxExpr = array.AsVectorizedEnumerable().Max();
        using var result = maxExpr.Evaluate(_context);
        Assert.Equal(expectedMax, ToBigInt(result));
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(AggregationData))]
    public void Min_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger expectedSum, BigInteger expectedMax, BigInteger expectedMin, int expectedCount)
    {
        using var array = CreateArray(input);
        var minExpr = array.AsVectorizedEnumerable().Min();
        using var result = minExpr.Evaluate(_context);
        Assert.Equal(expectedMin, ToBigInt(result));
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(AggregationData))]
    public void Count_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger expectedSum, BigInteger expectedMax, BigInteger expectedMin, int expectedCount)
    {
        using var array = CreateArray(input);
        var countExpr = array.AsVectorizedEnumerable().Count();
        using var result = countExpr.Evaluate(_context);
        Assert.Equal(expectedCount, (int)ToBigInt(result));
    }

    private static BigInteger TopBit(int numWords) => BigInteger.One << ((32 * numWords) - 1);

    private static BigInteger AllOnes(int numWords) => (BigInteger.One << (32 * numWords)) - 1;

    // Seeded values whose magnitudes stay below 2^(32N - 10), so that the sum of up to 1023 of them still fits in N words.
    private static SignedElement[] SeededElements(int length, int numWords, int seed)
    {
        var rng = new Random(seed);
        var result = new SignedElement[length];
        var bytes = new byte[(numWords * 4) - 1];
        for (var i = 0; i < length; i++)
        {
            rng.NextBytes(bytes);
            var magnitude = new BigInteger(bytes, isUnsigned: true) >> 2;
            result[i] = new SignedElement(magnitude, rng.Next(2) == 0);
        }

        return result;
    }

    public static IEnumerable<object[]> ReductionCases()
    {
        // 33 words (n = 32) make the block two warps, so a carry crosses from warp 0 into warp 1.
        foreach (var n in new[] { 1, 8, 32 })
        {
            // One element: there was no pass for length 1 before (#632), so the value has to come out of a single pass.
            yield return ["one 42", n, new SignedElement[] { 42 }];
            yield return ["one -7", n, new SignedElement[] { -7 }];
            yield return ["one 0", n, new SignedElement[] { 0 }];
            yield return ["one -0", n, new[] { s_negativeZero }];

            // The top bit of N words is part of the magnitude; an N-word two's complement reads it as a sign.
            yield return ["top bit", n, new[] { SignedElement.From(TopBit(n)) }];
            yield return ["top bit and 1", n, new[] { SignedElement.From(TopBit(n)), 1 }];
            yield return ["minus top bit and -1", n, new[] { SignedElement.From(-TopBit(n)), -1 }];

            // -2^(32N-1) has N - 1 zero low words: negating it back in the finalize kernel carries through all of them.
            yield return ["minus top bit", n, new[] { SignedElement.From(-TopBit(n)) }];

            // 2^(32N) - 1 has the top bit set, so even these sums, which stay within N words, need N + 1 words.
            yield return ["cancels within width", n,
                new[] { SignedElement.From(AllOnes(n)), SignedElement.From(-AllOnes(n)), 1 }];
            yield return ["cancels to +0", n, new SignedElement[] { 5, -5 }];
            yield return ["all ones cancel to +0", n, new[] { SignedElement.From(AllOnes(n)), SignedElement.From(-AllOnes(n)) }];

            // The running sum leaves N words (2^(32N) - 1 + 1) and comes back: only the final result has to fit.
            yield return ["leaves width and comes back", n, new[] { SignedElement.From(AllOnes(n)), 1, -1 }];

            // Pass boundaries: 255 / 256 elements are one pass of 256, 257 is two, 512 / 513 likewise.
            foreach (var length in new[] { 2, 255, 256, 257, 512, 513 })
            {
                yield return [$"seeded {length} (seed {632 + length})", n, SeededElements(length, n, 632 + length)];
            }
        }
    }

    // Evaluates and then disposes the reduction node (its buffers), unless the caller evaluates it again.
    private BigInteger EvaluateReduction(GpuBigIntExpr expr, bool disposeNode = true)
    {
        try
        {
            using var result = expr.Evaluate(_context);
            return ToBigInt(result);
        }
        finally
        {
            if (disposeNode)
            {
                DisposeNode(expr);
            }
        }
    }

    private static void DisposeNode(GpuBigIntExpr expr)
    {
        if (expr.Node is IDisposable node)
        {
            node.Dispose();
        }
    }

    // Checks the value and the sign flag: a zero result must not be flagged negative (BigInteger has no -0, so the
    // value alone cannot show it).
    private void AssertReduction(BigInteger expected, GpuBigIntExpr expr, string what)
    {
        try
        {
            using var result = expr.Evaluate(_context);
            Assert.True(expected == ToBigInt(result), $"{what}: expected {expected}, actual {ToBigInt(result)}");
            Assert.True(expected.Sign < 0 == result.IsNegative, $"{what}: IsNegative is {result.IsNegative}");
        }
        finally
        {
            DisposeNode(expr);
        }
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ReductionCases))]
    public void Reduction_SumMaxMin_MatchBigInteger(string label, int numWords, SignedElement[] values)
    {
        using var array = CreateSignedArray(values, numWords);
        var expected = values.Select(static v => v.Value).ToArray();

        // P1: Sum / Max / Min agree with System.Linq over the BigInteger values the test holds.
        // mutant: GpuBigIntVectorKernels.Reductions.cu.cs finalize `uint p = word == 0xFFFFFFFFu ? 1u : 0u;` -> `uint p = 0u;`
        // input: "minus top bit" (the +1 of the negation stops at word 0)
        // mutant: GpuBigIntVectorKernels.Reductions.cu.cs ReduceWarpMaxUInt64 `laneId + (int)offset < validLanes && otherVal > val` -> `otherVal > val`
        // equivalent for the reduction and comparison launches: Reduce and VectorCompareLaunch round every block up to
        // whole warps, so no lane is past the end of its warp. The guard stays as a second defense for other callers.
        // mutant: VectorReductionExpressions.cs `AccumulatorWords => NumWords + 1` -> `AccumulatorWords => NumWords`
        // input: "top bit and 1" (Max reads 2^(32N-1) as negative), "cancels within width" (Sum)
        // mutant: VectorReductionExpressions.cs pass plan `if (batchSize > 0)` -> `if (batchSize > 1)` (no pass for one element)
        // input: "one 42"
        // mutant: VectorReductionExpressions.cs `currentInputPtr = currentOutputPtr;` -> `_ = currentOutputPtr;`
        // input: "seeded 257"
        AssertReduction(expected.Aggregate(BigInteger.Zero, BigInteger.Add), array.AsVectorizedEnumerable().Sum(), $"{label}: Sum");
        AssertReduction(expected.Max(), array.AsVectorizedEnumerable().Max(), $"{label}: Max");
        AssertReduction(expected.Min(), array.AsVectorizedEnumerable().Min(), $"{label}: Min");

        // P2: the reductions do not change the input array (#632 B1 overwrote it).
        Assert.Equal(expected, ToHostArray(array));
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(1)]
    [InlineData(8)]
    public void Reduction_Identities_Hold(int numWords)
    {
        var values = SeededElements(300, numWords, 6320 + numWords);
        using var array = CreateSignedArray(values, numWords);

        // P4: Sum(v) == Sum(v.Take(k)) + Sum(v.Skip(k)), added on the host, for a split inside the second chunk of 256.
        var whole = EvaluateReduction(array.AsVectorizedEnumerable().Sum());
        var head = EvaluateReduction(array.AsVectorizedEnumerable().Take(270).Sum());
        var tail = EvaluateReduction(array.AsVectorizedEnumerable().Skip(270).Sum());
        Assert.Equal(whole, head + tail);
        Assert.Equal(values.Aggregate(BigInteger.Zero, static (acc, v) => acc + v.Value), whole);

        // P4: Max(v) == -Min(v * -1).
        using var minusOneMagnitude = new GpuBigUInt(_context, [1u]);
        var minusOne = new GpuBigInt(minusOneMagnitude, isNegative: true);
        var max = EvaluateReduction(array.AsVectorizedEnumerable().Max());
        var minOfNegated = EvaluateReduction(array.AsVectorizedEnumerable().SelectMultiply(minusOne).Min());
        Assert.Equal(max, -minOfNegated);

        // P5: evaluating the same expression twice gives the same value (the node reuses its buffers).
        var sum = array.AsVectorizedEnumerable().Sum();
        Assert.Equal(EvaluateReduction(sum, disposeNode: false), EvaluateReduction(sum));
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(-5, 3, 1)]
    [InlineData(-5, 3, 8)]
    [InlineData(5, -3, 8)]
    [InlineData(-5, -3, 8)]
    public void Reduction_EvaluateIntoDestination_WritesMagnitudeAndWrapsDestination(long a, long b, int numWords)
    {
        SignedElement[] values = [a, b];
        using var array = CreateSignedArray(values, numWords);
        var cases = new[]
        {
            ("Sum", array.AsVectorizedEnumerable().Sum(), (BigInteger)a + b),
            ("Max", array.AsVectorizedEnumerable().Max(), BigInteger.Max(a, b)),
            ("Min", array.AsVectorizedEnumerable().Min(), BigInteger.Min(a, b)),
        };

        foreach (var (name, expr, expected) in cases)
        {
            // The destination has the width of the result (Sum of two elements: one word more)
            var words = expr.Node.NumWords;
            using var destination = new GpuBigUInt(_context, Enumerable.Repeat(0xFFFFFFFFu, words).ToArray());

            var result = expr.Evaluate(destination);

            // P3: the destination holds the magnitude (not the two's complement or offset binary value, #632 B2), and
            // the returned value wraps the destination with the sign of the result.
            // mutant: GpuBigIntVectorKernels.Reductions.cu.cs `uint word = negative ? ~raw : raw;` -> `uint word = raw;`
            // input: (-5, 3) Sum
            // mutant: VectorReductionExpressions.cs `(_headerHost[0] & 1u) != 0u)` -> `false)`
            // input: (-5, 3) Sum
            Assert.True(BigInteger.Abs(expected) == destination.ToBigInteger(), $"{name}: destination");
            Assert.Equal(destination.DevicePointer, result.Value.DevicePointer);
            Assert.True(expected.Sign < 0 == result.IsNegative, $"{name}: sign");
        }
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(8, false)]
    [InlineData(8, true)]
    public void Reduction_SumOfTwoFullWidthElements_HasOneMoreWord(int numWords, bool negative)
    {
        // 2 * (2^(32N) - 1) needs N + 1 words: the sum of two elements has the bound 32 N + 1 bits, so the result has
        // N + 1 words and never overflows. Max and Min of the same values keep N words.
        // mutant: VectorReductionExpressions.cs VectorSumReductionExpression.NumWords -> ElementWords (the finalize
        // kernel reports the bit that does not fit)
        var value = SignedElement.From(negative ? -AllOnes(numWords) : AllOnes(numWords));
        using var array = CreateSignedArray([value, value], numWords);

        var sum = array.AsVectorizedEnumerable().Sum();
        Assert.Equal(numWords + 1, sum.Node.NumWords);
        Assert.Equal(2 * value.Value, EvaluateReduction(sum));
        Assert.Equal(value.Value, EvaluateReduction(array.AsVectorizedEnumerable().Max()));
        Assert.Equal(value.Value, EvaluateReduction(array.AsVectorizedEnumerable().Min()));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Reduction_NegativeSumOneWordWider_CarriesThroughEveryWord()
    {
        // -(2^1024 - 1) - 1 = -2^1024 needs 33 words. Its accumulator has 32 zero low words, so negating it in the
        // finalize kernel carries through every word (across the two warps of the 64-thread block) into word 32.
        // mutant: GpuBigIntVectorKernels.Reductions.cu.cs finalize `uint p = word == 0xFFFFFFFFu ? 1u : 0u;` -> `uint p = 0u;`
        // input: this test (word 32 stays 0)
        var values = new[] { SignedElement.From(-AllOnes(32)), -1 };
        using var array = CreateSignedArray(values, 32);

        Assert.Equal(-(BigInteger.One << 1024), EvaluateReduction(array.AsVectorizedEnumerable().Sum()));
        AssertReduction(values.Min(static v => v.Value), array.AsVectorizedEnumerable().Min(), "Min");
        AssertReduction(values.Max(static v => v.Value), array.AsVectorizedEnumerable().Max(), "Max");
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Reduction_FailureIntoDestination_KeepsCallerBuffer()
    {
        // The caller owns the destination: a failure after the launch (Max of a vector that Where emptied on the
        // device) must not release it. The workspace reuses a released buffer of the same word count first (LIFO), so
        // a release would hand the destination's memory to the next 8-word allocation.
        // mutant: VectorReductionExpressions.cs `catch when (!targetResult.HasValue)` -> `catch`
        // input: this test
        using var array = CreateSignedArray([SignedElement.From(AllOnes(8)), 1], 8);
        using var bound = CreateScalar(AllOnes(8));
        var v = array.AsVectorizedEnumerable();
        using var destination = new GpuBigUInt(_context, 8);

        Assert.Throws<InvalidOperationException>(() => v.Where(v.IsGreaterThan(bound)).Max().Evaluate(destination));

        using var next = new GpuBigUInt(_context, 8);
        Assert.NotEqual(destination.DevicePointer, next.DevicePointer);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Reduction_FailureWithoutDestination_ReleasesItsBuffer()
    {
        // Without a destination the reduction allocates the result buffer itself; when it throws after the launch
        // nobody else can release it. A released 8-word buffer is reused first (LIFO), so the probe's memory comes
        // back after the throw only if the reduction released the buffer it took.
        // mutant: VectorReductionExpressions.cs `destination.Dispose();` -> removed
        // input: this test
        using var array = CreateSignedArray([SignedElement.From(AllOnes(8)), 1], 8);
        using var bound = CreateScalar(AllOnes(8));
        var v = array.AsVectorizedEnumerable();
        using var max = v.Where(v.IsGreaterThan(bound)).Max();
        IntPtr released;
        using (var probe = new GpuBigUInt(_context, 8))
        {
            released = probe.DevicePointer;
        }

        Assert.Throws<InvalidOperationException>(() => max.Evaluate(_context));

        using var next = new GpuBigUInt(_context, 8);
        Assert.Equal(released, next.DevicePointer);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(1)]
    [InlineData(7)]
    public void Reduction_NarrowerDestination_ThrowsArgumentException(int destinationWords)
    {
        using var array = CreateArray([1, 2]);
        uint[] filled = [.. Enumerable.Repeat(0xDEADBEEFu, destinationWords)];
        using var destination = new GpuBigUInt(_context, filled);

        // Sum of two 8-word elements has 9 words, Max 8: a narrower destination is rejected and not touched
        // mutant: VectorReductionExpressions.cs ThrowIfCannotEvaluate `target.NumWords < NumWords` -> `false`
        Assert.Throws<ArgumentException>(() => array.AsVectorizedEnumerable().Sum().Evaluate(destination));
        Assert.Throws<ArgumentException>(() => array.AsVectorizedEnumerable().Max().Evaluate(destination));
        Assert.Equal(filled, destination.ToHostArray());
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Reduction_WiderDestination_GetsZerosAboveTheResult()
    {
        // A destination wider than the result, as for the scalar expressions: the magnitude in the first words, zeros
        // above them
        // mutant: VectorReductionExpressions.cs Evaluate without `SignedRoot.ClearAbove(destination, NumWords)`
        using var array = CreateSignedArray([-5, -7], 2);
        foreach (var (name, expr, expected) in new[]
        {
            ("Sum", array.AsVectorizedEnumerable().Sum(), (BigInteger)(-12)),
            ("Max", array.AsVectorizedEnumerable().Max(), (BigInteger)(-5))
        })
        {
            using var destination = new GpuBigUInt(_context, [.. Enumerable.Repeat(0xDEADBEEFu, 6)]);
            var result = expr.Evaluate(destination);
            Assert.True(expected.Sign < 0 == result.IsNegative, name);
            Assert.True(result.Value.NumWords == 6, name);
            var limbs = new uint[6];
            BigInteger.Abs(expected).WriteLimbs(limbs);
            Assert.Equal(limbs, destination.ToHostArray());
            expr.Dispose();
        }
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void MaxMin_ReportTheElementBitBound()
    {
        // Max and Min choose an element, so their bound is the element's: a ZipAdd of two one-word vectors has 33 bits
        // mutant: VectorReductionExpressions.cs base `Bits => _vector.BitsPerElement` -> `32L * ElementWords`
        using var ones = CreateArray([1, 2], 1);
        var sum = ones.AsVectorizedEnumerable().ZipAdd(ones.AsVectorizedEnumerable());
        var max = sum.Max();
        Assert.Equal(33L, ((IBitBoundedExpression)max.Node).Bits);
        max.Dispose();
    }

    [Fact]
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.Boundary)]
    public void Reduction_ThreePasses_MatchBigInteger()
    {
        // 65537 elements need three passes of 256 (65537 -> 257 -> 2 -> 1). Magnitudes below 2^40 keep the sum
        // (below 2^57) inside 2 words.
        var rng = new Random(65537);
        var values = new SignedElement[65537];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = new SignedElement(rng.NextInt64(1L << 40), rng.Next(2) == 0);
        }

        using var array = CreateSignedArray(values, 2);
        var expected = values.Select(static v => v.Value).ToArray();

        Assert.Equal(expected.Aggregate(BigInteger.Zero, BigInteger.Add), EvaluateReduction(array.AsVectorizedEnumerable().Sum()));
        Assert.Equal(expected.Max(), EvaluateReduction(array.AsVectorizedEnumerable().Max()));
        Assert.Equal(expected.Min(), EvaluateReduction(array.AsVectorizedEnumerable().Min()));
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(8)]
    public void Reduction_EmptyVector_MatchesSystemLinq(int numWords)
    {
        // The reference: System.Linq over an empty sequence. Max / Min throw InvalidOperationException, Sum and Count
        // are 0 (#633 decision 2).
        var reference = Array.Empty<BigInteger>();
        var expectedMax = Record.Exception(() => reference.Max());
        var expectedMin = Record.Exception(() => reference.Min());
        Assert.IsType<InvalidOperationException>(expectedMax);
        Assert.IsType<InvalidOperationException>(expectedMin);

        using var empty = new GpuBigIntArray(_context, 0, numWords);

        var max = Assert.Throws<InvalidOperationException>(() => empty.AsVectorizedEnumerable().Max().Evaluate(_context));
        var min = Assert.Throws<InvalidOperationException>(() => empty.AsVectorizedEnumerable().Min().Evaluate(_context));
        Assert.Equal(expectedMax.Message, max.Message);
        Assert.Equal(expectedMin.Message, min.Message);

        using var sum = empty.AsVectorizedEnumerable().Sum().Evaluate(_context);
        Assert.Equal(reference.Aggregate(BigInteger.Zero, BigInteger.Add), ToBigInt(sum));
        Assert.False(sum.IsNegative);

        using var count = empty.AsVectorizedEnumerable().Count().Evaluate(_context);
        Assert.Equal(new BigInteger(reference.Length), ToBigInt(count));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Reduction_EmptyMaxMinIntoDestination_ThrowBeforeTouchingDestination()
    {
        // An empty Max / Min throws before anything is launched, so the destination keeps what the caller put there.
        // mutant: VectorReductionExpressions.cs `if (_passes.Count == 0 && EmptyIsError)` -> `if (_passes.Count < 0 && EmptyIsError)`
        // input: this test (the finalize kernel clears the destination before the header check throws)
        using var empty = new GpuBigIntArray(_context, 0, 8);
        var filled = Enumerable.Repeat(0xFFFFFFFFu, 8).ToArray();
        using var destination = new GpuBigUInt(_context, filled);

        Assert.Throws<InvalidOperationException>(() => empty.AsVectorizedEnumerable().Max().Evaluate(destination));
        Assert.Equal(filled, destination.ToHostArray());
        Assert.Throws<InvalidOperationException>(() => empty.AsVectorizedEnumerable().Min().Evaluate(destination));
        Assert.Equal(filled, destination.ToHostArray());
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(8)]
    public void Reduction_EmptyAfterSkipAndSelect_MatchesSystemLinq(int numWords)
    {
        // An empty vector that is not a leaf: Skip(length) leaves nothing, and SelectAdd would launch an empty grid.
        // The reference is System.Linq over the same empty selection.
        // mutant: VectorBinaryScalarExpressions.cs `if (evalCount > 0)` -> `if (evalCount >= 0)`
        // input: the direct Evaluate below (CudaException: invalid value)
        // mutant: VectorReductionExpressions.cs `if (_passes.Count > 0)` (evaluate the vector) -> `if (_passes.Count >= 0)`
        // input: none alone, the empty SelectAdd now launches nothing; with the first mutant as well, Sum fails
        SignedElement[] values = [3, -4, 5];
        using var array = CreateSignedArray(values, numWords);
        using var oneMagnitude = new GpuBigUInt(_context, [1u]);
        var one = new GpuBigInt(oneMagnitude, isNegative: false);
        var reference = values.Skip(values.Length).Select(static v => v.Value + 1).ToArray();

        using (var direct = array.AsVectorizedEnumerable().Skip(values.Length).SelectAdd(one))
        {
            Assert.Equal(0, direct.Evaluate(_context).Length);
        }

        AssertReduction(
            reference.Aggregate(BigInteger.Zero, BigInteger.Add),
            array.AsVectorizedEnumerable().Skip(values.Length).SelectAdd(one).Sum(),
            "Sum");
        Assert.IsType<InvalidOperationException>(Record.Exception(() => reference.Max()));
        Assert.Throws<InvalidOperationException>(() =>
            EvaluateReduction(array.AsVectorizedEnumerable().Skip(values.Length).SelectAdd(one).Max()));
        Assert.Throws<InvalidOperationException>(() =>
            EvaluateReduction(array.AsVectorizedEnumerable().Skip(values.Length).SelectAdd(one).Min()));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void Reduction_EmptyVectorSumIntoDestination_ClearsDestination()
    {
        // The sum of nothing is +0: the destination is cleared even when it held another value, and the returned
        // value wraps it.
        // mutant: GpuBigIntVectorKernels.Reductions.cu.cs finalize, count 0 `ctx.Destination[idx] = 0u;` -> `ctx.Destination[idx] = ctx.Destination[idx];`
        // input: this test (the destination keeps 0xFFFFFFFF)
        using var empty = new GpuBigIntArray(_context, 0, 8);
        using var destination = new GpuBigUInt(_context, Enumerable.Repeat(0xFFFFFFFFu, 8).ToArray());

        var result = empty.AsVectorizedEnumerable().Sum().Evaluate(destination);

        Assert.Equal(BigInteger.Zero, destination.ToBigInteger());
        Assert.Equal(destination.DevicePointer, result.Value.DevicePointer);
        Assert.False(result.IsNegative);
    }
}
