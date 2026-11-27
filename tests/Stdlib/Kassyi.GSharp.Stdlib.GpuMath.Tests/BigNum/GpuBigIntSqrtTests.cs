namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntSqrtTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Sqrt_SmallValues_ProducesExactFloorRoot()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        
        using var a = new GpuBigUInt(ctx, new uint[] { 10000, 0, 0, 0 });
        using var result = new GpuBigUInt(ctx, 4);
        
        GpuBigIntDivRem.Sqrt(a, result);
        
        Assert.Equal(100u, result.ToHostArray()[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Sqrt_LargeAllOnesOperand_IsFloorOfSquareRoot()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(512, GpuExecutionEngine.GSharpAot);

        var aArr = new uint[512];
        Array.Fill(aArr, 0xFFFFFFFFu);

        using var a = new GpuBigUInt(ctx, aArr);
        using var result = new GpuBigUInt(ctx, 512);

        GpuBigIntDivRem.Sqrt(a, result);

        AssertIsFloorSquareRoot(a.ToBigInteger(), result.ToBigInteger(), "512 words of ones");
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(15u)]
    [InlineData(16u)]
    [InlineData(65535u * 65535u)]
    [InlineData(uint.MaxValue)]
    public void Sqrt_SingleWordValueInWideBuffer_ReturnsFloorRoot(uint value)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);

        // Only word 0 carries data; the single-word fast path must still read exactly that word.
        var words = new uint[64];
        words[0] = value;
        using var a = new GpuBigUInt(ctx, words);
        using var result = new GpuBigUInt(ctx, 64);

        GpuBigIntDivRem.Sqrt(a, result);

        Assert.Equal(FloorSqrt(value), result.ToBigInteger());
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(128, 0)]
    [InlineData(128, -1)]
    [InlineData(200, 12345)]
    [InlineData(256, -1)]
    [InlineData(300, 1)]
    public void Sqrt_MultiWordValue_ReturnsFloorRoot(int bits, int offset)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(512, GpuExecutionEngine.GSharpAot);

        var value = (BigInteger.One << bits) + offset;
        const int Words = 24;
        using var a = GpuBigUInt.FromBigInteger(ctx, value, Words);
        using var result = new GpuBigUInt(ctx, Words);

        GpuBigIntDivRem.Sqrt(a, result);

        Assert.Equal(FloorSqrt(value), result.ToBigInteger());
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(1)]
    public void Sqrt_NarrowerThanTheRoot_ThrowsArgumentException(int resultWords)
    {
        // The root of a 4-word operand needs 2 words. A one-word value takes the single-word path, whose own
        // operations never check the sizes, so only this check rejects the buffer.
        // mutant: GpuBigIntDivRem.Sqrt.cs SqrtCore `ThrowIfResultNarrower` removed
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        using var a = new GpuBigUInt(ctx, new uint[] { 9, 0, 0, 0 });
        using var result = new GpuBigUInt(ctx, resultWords);

        Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.Sqrt(a, result));
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0u, 0u)]
    [InlineData(10000u, 100u)]
    public void Sqrt_PrefilledResultBuffer_IsOverwritten(uint value, uint expectedRoot)
    {
        // The result buffer is filled with 0xFFFFFFFF first; every word above the root must be cleared.
        // mutant: GpuBigIntDivRem.Sqrt.cs:217 `resultBuffer.Clear()` removed (value 0)
        // mutant: GpuBigIntDivRem.Sqrt.cs:240 `resultBuffer.Clear()` removed (value 10000)
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        using var a = new GpuBigUInt(ctx, new uint[] { value, 0, 0, 0 });
        using var result = new GpuBigUInt(ctx, Enumerable.Repeat(uint.MaxValue, 4).ToArray());

        GpuBigIntDivRem.Sqrt(a, result);

        Assert.Equal(new BigInteger(expectedRoot), result.ToBigInteger());
    }

    /// <summary>
    /// SqrtFloor inputs: the kind of operand and the root words <c>n</c>. Every width that changes the margin
    /// <c>m = ceil(n / 2)</c> of the approximation, odd and even, and the ends of its range.
    /// </summary>
    public static TheoryData<string, int> SqrtFloorInputs()
    {
        var data = new TheoryData<string, int>();
        foreach (var kind in new[] { "Random", "RandomOddLength", "Square", "SquareMinus1", "SquarePlus2R", "Lowest", "Highest" })
        {
            foreach (var n in new[] { 1, 2, 3, 4, 5, 8, 16, 17 })
            {
                data.Add(kind, n);
            }
        }

        return data;
    }

    // SqrtFloor gives floor(sqrt(a)) for an operand of 2n words with bit length 64n or 64n - 1 (the input of the
    // big-float Sqrt): the root agrees with the test's own integer square root and satisfies r² <= a < (r + 1)².
    // The squares, r² - 1 and r² + 2r are where an approximation one below the floor has to step up, or must not.
    // mutant: GpuBigIntDivRem.Sqrt.cs:172 `SqrtFloorCorrection(...)` removed (Square, n = 8, 16, 17)
    // mutant: GpuBigIntDivRem.Sqrt.cs:180 `(rootWords + 1) / 2` -> `Math.Max(1, (rootWords + 1) / 2 - 1)` (SquarePlus2R
    //         and SquareMinus1 at
    // n = 4, 8, 16: the estimate is more than one below, beyond what the design allows)
    // mutant: GpuBigIntSqrtKernels.cu.cs:178 `AddSmallSigned(trial, slackWords, -1)` removed (Highest, n = 16, 17)
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SqrtFloorInputs))]
    public void SqrtFloor_IsFloorOfSquareRoot(string kind, int n)
    {
        CudaCapability.EnsureGpuAvailable();
        var a = BuildSqrtFloorOperand(kind, n);
        using var ctx = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, 2 * n);
        using var root = new GpuBigUInt(ctx, n);

        GpuBigIntDivRem.SqrtFloor(operand, root);

        var r = root.ToBigInteger();
        Assert.Equal(FloorSqrt(a), r);
        Assert.True(r * r <= a && a < (r + 1) * (r + 1), $"{kind} n={n}: {r:X} is not floor(sqrt({a:X}))");
    }

    /// <summary>
    /// Inputs of the SqrtFloor estimate: the kind of operand and the root words <c>n</c> (1 to 17, 32 and 33).
    /// </summary>
    public static TheoryData<string, int> SqrtFloorEstimateInputs()
    {
        var data = new TheoryData<string, int>();
        foreach (var kind in new[] { "Random", "RandomOddLength", "Square", "SquareMinus1", "SquarePlus2R", "TopSquareMaxLow", "Lowest", "Highest" })
        {
            for (var n = 1; n <= 17; n++)
            {
                data.Add(kind, n);
            }

            data.Add(kind, 32);
            data.Add(kind, 33);
        }

        return data;
    }

    // The estimate of SqrtFloor, recomputed on the host with BigInteger from the margin and the shift
    // that the product uses: x = floor(2^B / sqrt(A_top)) = isqrt(floor(2^(2B) / A_top)) and r' = floor(A_top·x / 2^s).
    // P1: r' is floor(sqrt(A)) or one less, so the device correction needs at most one step.
    // P2: the error of the approximation, sqrt(A) - A_top·x / 2^s, lies in [0, 2^-16 + 2^-32), the bound the design
    // derives. With a margin one word smaller (odd n) the error can reach 1; a gap of 2 then needs an input that no seeded
    // search found, so P2 is what catches it.
    // mutant: GpuBigIntDivRem.Sqrt.cs:180 `(rootWords + 1) / 2` -> `rootWords / 2` (every odd n with TopSquareMaxLow
    //         and others:
    // P2; P1 holds for every input here)
    // mutant: GpuBigIntDivRem.Sqrt.cs:187 `+ 48` -> `+ 47` (every input: the estimate is twice the root)
    [Theory]
    [Category(TestCategory.Stdlib)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SqrtFloorEstimateInputs))]
    public void SqrtFloorEstimate_IsFloorOrOneBelow(string kind, int n)
    {
        var a = BuildSqrtFloorOperand(kind, n);
        var m = GpuBigIntDivRem.SqrtFloorMarginWords(n);
        var s = GpuBigIntDivRem.SqrtFloorEstimateShift(n, m);
        var top = a >> (32 * (n - 1));
        var b = 32 * (n + 1 + m);
        var x = FloorSqrt((BigInteger.One << (2 * b)) / top);
        var approximation = top * x;
        var estimate = approximation >> s;
        var root = FloorSqrt(a);

        var gap = root - estimate;
        Assert.True(gap >= 0 && gap <= 1, $"{kind} n={n}: floor(sqrt(A)) - r' = {gap}");

        // P3: the approximate inverse root SqrtFloor uses may be up to ApproximateInverseSqrtMaxError below x; the
        // estimate from the lowest such x is still floor(sqrt(A)) or one less.
        var lowestEstimate = (top * (x - GpuBigIntDivRem.ApproximateInverseSqrtMaxError)) >> s;
        var lowestGap = root - lowestEstimate;
        Assert.True(lowestGap >= 0 && lowestGap <= 1,$"{kind} n={n}: from x - E, floor(sqrt(A)) - r' = {lowestGap}");

        // approximation / 2^s <= sqrt(A) < (approximation + 2^(s-16) + 2^(s-32)) / 2^s, squared and scaled by 2^(2s).
        var scaled = a << (2 * s);
        var limit = approximation + (BigInteger.One << (s - 16)) + (BigInteger.One << (s - 32));
        Assert.True(
            approximation * approximation <= scaled && scaled < limit * limit,
            $"{kind} n={n}: the error of the approximation is outside [0, 2^-16 + 2^-32)");
    }

    // The operand of SqrtFloor must be twice as wide as the root.
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void SqrtFloor_OperandNotTwiceTheRoot_ThrowsArgumentException()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);
        using var operand = new GpuBigUInt(ctx, 7);
        using var root = new GpuBigUInt(ctx, 4);

        Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.SqrtFloor(operand, root));
    }

    internal static BigInteger BuildSqrtFloorOperand(string kind, int n)
    {
        // Seed 661 + 37n keeps the values stable across runs.
        var random = new Random(661 + 37 * n);
        BigInteger RandomBits(int bits)
        {
            var bytes = new byte[(bits + 7) / 8 + 1];
            random.NextBytes(bytes);
            bytes[^1] = 0;
            return (new BigInteger(bytes) >> (8 * (bytes.Length - 1) - bits)) | (BigInteger.One << (bits - 1));
        }

        var p = 32 * n;
        var r = RandomBits(p) | 1;
        var j = 32 * (n - 1);
        var t = RandomBits(16 * (n + 1));
        return kind switch
        {
            // A_top = t² is a perfect square (its inverse root has no truncation) and the low words are all ones.
            "TopSquareMaxLow" => ((t * t) << j) + (BigInteger.One << j) - 1,
            "Random" => RandomBits(2 * p),
            "RandomOddLength" => RandomBits(2 * p - 1),
            "Square" => r * r,
            "SquareMinus1" => r * r - 1,
            "SquarePlus2R" => r * r + 2 * r,
            "Lowest" => BigInteger.One << (2 * p - 2),
            "Highest" => (BigInteger.One << (2 * p)) - 1,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown SqrtFloor input."),
        };
    }

    /// <summary>
    /// Sqrt inputs above one word: the kind of operand and its words (2 to 8, around 64, and 1000). An odd word count
    /// leaves 64r - L = 32 for a full top word, an even one 0.
    /// </summary>
    public static TheoryData<string, int> SqrtMultiWordInputs()
    {
        var data = new TheoryData<string, int>();
        string[] kinds =
        [
            "Random", "AllOnes", "PowerOfTwo", "PowerOfTwoMinusOne", "Square", "SquareMinusOne", "SquarePlusOne",
            "OddBitLength", "EvenBitLength"
        ];
        foreach (var kind in kinds)
        {
            foreach (var words in new[] { 2, 3, 4, 5, 6, 7, 8, 63, 64, 65, 1000 })
            {
                data.Add(kind, words);
            }
        }

        return data;
    }

    // Sqrt of an operand above 32 bits normalizes it with an even shift for SqrtFloor and shifts the root back. The
    // squares and their neighbours are where a root one off shows; PowerOfTwoMinusOne and OddBitLength have an odd bit
    // length (64r - L odd), PowerOfTwo, AllOnes and EvenBitLength an even one.
    // mutant: GpuBigIntDivRem.Sqrt.cs:257 `/ 2` -> `/ 2 + 1` (every input: the top bit leaves the 2r words)
    // mutant: GpuBigIntDivRem.Sqrt.cs:268 `-halfShift` -> `0` (every input with 64r - L >= 2: odd word counts and
    //         EvenBitLength; OddBitLength of an even word count has 64r - L = 1, so k = 0)
    // mutant: GpuBigIntDivRem.Sqrt.cs:256 `(actualBits + 63) / 64` -> `actualBits / 64` (every input whose bit length
    //         is not a multiple of 64)
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SqrtMultiWordInputs))]
    public void Sqrt_MultiWordOperand_IsFloorOfSquareRoot(string kind, int words)
    {
        CudaCapability.EnsureGpuAvailable();
        var a = BuildSqrtOperand(kind, words);
        using var ctx = new GpuWorkspace(16 * words + 4096, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, words);
        using var result = new GpuBigUInt(ctx, words);

        GpuBigIntDivRem.Sqrt(operand, result);

        AssertIsFloorSquareRoot(a, result.ToBigInteger(), $"{kind} words={words}");
    }

    // The gap 64r - L between the bit length and the root words: 0 (no shift), 1 (odd, k = 0 keeps the bit length
    // 64r - 1), 2 (k = 1) and 63 (the largest, k = 31), each in a 4-word operand; and the one-word roots (L = 33 to
    // 64, k = 15 down to 0), the shortest operands that take this path.
    // mutant: GpuBigIntDivRem.Sqrt.cs:257 `/ 2` -> `/ 2 + 1` (every bit length)
    // mutant: GpuBigIntDivRem.Sqrt.cs:268 `-halfShift` -> `0` (126, 65 and the one-word roots below 63)
    // mutant: GpuBigIntDivRem.Sqrt.cs:256 `(actualBits + 63) / 64` -> `actualBits / 64` (127, 126, 65 and the
    //         one-word roots below 64)
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(128)]
    [InlineData(127)]
    [InlineData(126)]
    [InlineData(65)]
    [InlineData(64)]
    [InlineData(63)]
    [InlineData(62)]
    [InlineData(61)]
    [InlineData(48)]
    [InlineData(47)]
    [InlineData(40)]
    [InlineData(34)]
    [InlineData(33)]
    public void Sqrt_NormalizationGap_IsFloorOfSquareRoot(int bitLength)
    {
        CudaCapability.EnsureGpuAvailable();
        var a = RandomWithBitLength(new Random(911 + bitLength), bitLength);
        using var ctx = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, 4);
        using var result = new GpuBigUInt(ctx, 4);

        GpuBigIntDivRem.Sqrt(operand, result);

        AssertIsFloorSquareRoot(a, result.ToBigInteger(), $"bit length {bitLength}");
    }

    /// <summary>
    /// Result buffers of Sqrt for an 8-word operand: the kind of buffer and the bit length of the operand (131: a root
    /// of 3 words, below the minimum of 4; 256: a root of exactly 4 words).
    /// </summary>
    public static TheoryData<string, int> SqrtResultBufferInputs()
    {
        var data = new TheoryData<string, int>();
        foreach (var kind in new[] { "SameWidth", "Wider", "MinimumWidth", "OperandItself" })
        {
            data.Add(kind, 131);
            data.Add(kind, 256);
        }

        return data;
    }

    // Any result buffer from ceil(8 / 2) = 4 words up receives the root and zeros above it: each separate buffer is
    // filled with ones first, and the whole buffer is read back. The operand itself may also be the result buffer.
    // mutant: GpuBigIntDivRem.Sqrt.cs:268 `resultBuffer` -> `ResultBuffers.Head(resultBuffer, rootWords)` (131 with
    //         SameWidth, Wider, MinimumWidth and OperandItself: the words above the root keep their old value)
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SqrtResultBufferInputs))]
    public void Sqrt_ResultBufferWidth_ReceivesRootWithZerosAbove(string kind, int bitLength)
    {
        CudaCapability.EnsureGpuAvailable();
        const int OperandWords = 8;
        var a = RandomWithBitLength(new Random(919 + bitLength), bitLength);
        var resultWords = kind switch
        {
            "SameWidth" or "OperandItself" => OperandWords,
            "Wider" => OperandWords + 4,
            "MinimumWidth" => OperandWords / 2,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown result buffer.")
        };
        using var ctx = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, OperandWords);
        using var separate = new GpuBigUInt(ctx, Enumerable.Repeat(uint.MaxValue, resultWords).ToArray());
        var result = kind == "OperandItself" ? operand : separate;

        GpuBigIntDivRem.Sqrt(operand, result);

        Assert.Equal(resultWords, result.NumWords);
        AssertIsFloorSquareRoot(a, result.ToBigInteger(), $"{kind} bit length {bitLength}");
    }

    // The minimum result width follows from the operand's width, not from its value: one word below ceil(8 / 2) = 4 is
    // rejected although the root of this 131-bit value fits in 3 words.
    // mutant: GpuBigIntDivRem.Sqrt.cs:212 `ResultBuffers.ThrowIfNarrower(...)` removed (the root is written into 3
    //         words without an exception)
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void Sqrt_ResultOneWordBelowOperandMinimum_ThrowsArgumentException()
    {
        CudaCapability.EnsureGpuAvailable();
        var a = RandomWithBitLength(new Random(919 + 131), 131);
        using var ctx = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
        using var operand = GpuBigUInt.FromBigInteger(ctx, a, 8);
        using var result = new GpuBigUInt(ctx, 3);

        Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.Sqrt(operand, result));
    }

    internal static void AssertIsFloorSquareRoot(BigInteger a, BigInteger r, string label)
    {
        // r² <= a < (r + 1)² defines floor(sqrt(a)) without a reference implementation; the test's own integer square
        // root must give the same value.
        Assert.True(r.Sign >= 0 && r * r <= a && a < (r + 1) * (r + 1), $"{label}: the result is not floor(sqrt(a))");
        Assert.Equal(FloorSqrt(a), r);
    }

    internal static BigInteger BuildSqrtOperand(string kind, int words)
    {
        var bits = 32 * words;
        // Seed 907 + 31 * words keeps the values stable across runs; every kind draws the same values.
        var random = new Random(907 + 31 * words);
        var randomFull = RandomWithBitLength(random, bits);
        var s = RandomWithBitLength(random, bits / 2);
        var low = RandomWithBitLength(random, bits - 3);
        var top = BigInteger.One << (bits - 1);
        return kind switch
        {
            "Random" => randomFull,
            "AllOnes" => (BigInteger.One << bits) - 1,
            "PowerOfTwo" => top,
            "PowerOfTwoMinusOne" => top - 1,
            "Square" => s * s,
            "SquareMinusOne" => s * s - 1,
            "SquarePlusOne" => s * s + 1,
            // The same low bits under a top bit at bits - 2 (bit length bits - 1, odd) or bits - 3 (bits - 2, even).
            "OddBitLength" => low | (BigInteger.One << (bits - 2)),
            "EvenBitLength" => low | (BigInteger.One << (bits - 3)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Sqrt input."),
        };
    }

    internal static BigInteger RandomWithBitLength(Random random, int bitLength)
    {
        var bytes = new byte[bitLength / 8 + 2];
        random.NextBytes(bytes);
        bytes[^1] = 0;
        var top = BigInteger.One << (bitLength - 1);
        return (new BigInteger(bytes) & (top - 1)) | top;
    }

    internal static BigInteger FloorSqrt(BigInteger value)
    {
        if (value < 2)
        {
            return value;
        }

        var x = BigInteger.One << (int)((value.GetBitLength() + 1) / 2);
        while (true)
        {
            var next = (x + (value / x)) >> 1;
            if (next >= x)
            {
                return x;
            }

            x = next;
        }
    }
}
