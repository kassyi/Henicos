

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntSqrtTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Sqrt_SmallValues()
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
    public void Test_Sqrt_LargeValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(512, GpuExecutionEngine.GSharpAot);
        
        var aArr = new uint[512];
        Array.Fill(aArr, 0xFFFFFFFFu);
        
        using var a = new GpuBigUInt(ctx, aArr);
        using var result = new GpuBigUInt(ctx, 512);
        
        GpuBigIntDivRem.Sqrt(a, result);
        
        Assert.True(result.NumWords > 0);
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
    public void Test_Sqrt_SingleWordValueInWideBuffer_ReturnsFloorRoot(uint value)
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
    public void Test_Sqrt_MultiWordValue_ReturnsFloorRoot(int bits, int offset)
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
    [InlineData(5)]
    [InlineData(3)]
    public void Sqrt_MismatchedBufferSizes_ThrowsArgumentException(int resultWords)
    {
        // The result buffer has as many words as the operand. A one-word value takes the single-word path, whose
        // own operations never check the sizes, so only this check rejects the buffer.
        // mutant: GpuBigIntDivRem.Sqrt.cs:141 `ThrowIfSizeMismatch(leftOperand, resultBuffer)` removed
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
        // mutant: GpuBigIntDivRem.Sqrt.cs:146 `resultBuffer.Clear()` removed (value 0)
        // mutant: GpuBigIntDivRem.Sqrt.cs:170 `resultBuffer.Clear()` removed (value 10000)
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
    // mutant: GpuBigIntDivRem.Sqrt.cs:109 `SqrtFloorCorrection(...)` removed (Square, n = 8, 16, 17)
    // mutant: GpuBigIntDivRem.Sqrt.cs:117 `(rootWords + 1) / 2` -> `Math.Max(1, (rootWords + 1) / 2 - 1)` (SquarePlus2R and SquareMinus1 at
    // n = 4, 8, 16: the estimate is more than one below, beyond what the design allows)
    // mutant: GpuBigIntSqrtKernels.cu.cs:176 `AddSmallSigned(trial, slackWords, -1)` removed (Highest, n = 16, 17)
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

    // The estimate of SqrtFloor (design §4.3.1), recomputed on the host with BigInteger from the margin and the shift
    // that the product uses: x = floor(2^B / sqrt(A_top)) = isqrt(floor(2^(2B) / A_top)) and r' = floor(A_top·x / 2^s).
    // P1: r' is floor(sqrt(A)) or one less, so the device correction needs at most one step.
    // P2: the error of the approximation, sqrt(A) - A_top·x / 2^s, lies in [0, 2^-16 + 2^-32), the bound the design
    // derives. With a margin one word smaller (odd n) the error can reach 1; a gap of 2 then needs an input that no seeded
    // search found, so P2 is what catches it.
    // mutant: GpuBigIntDivRem.Sqrt.cs:117 `(rootWords + 1) / 2` -> `rootWords / 2` (every odd n with TopSquareMaxLow and others:
    // P2; P1 holds for every input here)
    // mutant: GpuBigIntDivRem.Sqrt.cs:124 `+ 48` -> `+ 47` (every input: the estimate is twice the root)
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

    private static BigInteger BuildSqrtFloorOperand(string kind, int n)
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

    private static BigInteger FloorSqrt(BigInteger value)
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
