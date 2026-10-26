namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Buffer checks, signed addition, resizing shifts and powers of ten of <see cref="GpuBigIntMath"/>.
/// </summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class GpuBigIntMathTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBigIntMathTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    /// <summary>
    /// Operation name and the word counts of (left, right, result). Exactly one count differs from 4 in each row,
    /// once in every position, so each comparison of the size check is needed.
    /// </summary>
    public static TheoryData<string, int, int, int> MismatchedSizes()
    {
        var data = new TheoryData<string, int, int, int>();
        foreach (var operation in new[] { "Add", "Sub", "BitwiseAnd", "BitwiseOr", "BitwiseXor", "Gcd", "ModInverse" })
        {
            data.Add(operation, 5, 4, 4);
            data.Add(operation, 4, 5, 4);
            data.Add(operation, 4, 4, 5);
            data.Add(operation, 4, 4, 3);
        }

        // Unary operations: (operand, -, result) with a longer and a shorter result.
        foreach (var operation in new[] { "DivideByUInt32", "ShiftLeft", "ShiftRight", "BitwiseNot" })
        {
            data.Add(operation, 4, 0, 5);
            data.Add(operation, 4, 0, 3);
        }

        // IsLessThan compares two operands of the same size.
        data.Add("IsLessThan", 4, 5, 0);
        data.Add("IsLessThan", 5, 4, 0);

        // Mul needs exactly left + right words.
        data.Add("Mul", 4, 4, 7);
        data.Add("Mul", 4, 4, 9);
        return data;
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(MismatchedSizes))]
    public void Operations_MismatchedBufferSizes_ThrowArgumentException(string operation, int leftWords, int rightWords, int resultWords)
    {
        // A size mismatch is rejected on the host instead of running the kernel past a buffer.
        // mutant: GpuBigIntMath.cs:43 / :95 / :196 / :210 / :333 / :347 / :361 `ThrowIfSizeMismatch(...)` removed (Add, Sub, Gcd, ModInverse, And, Or, Xor)
        // mutant: GpuBigIntMath.cs:121 `!=` -> `==` (Mul); :182 / :223 / :236 / :373 removed (DivideByUInt32, shifts, Not)
        // mutant: GpuBigIntMath.cs:386 `ThrowIfSizeMismatch(leftOperand, rightOperand)` removed (IsLessThan)
        using var left = new GpuBigUInt(_workspace, Words(leftWords, 7u));
        using var right = new GpuBigUInt(_workspace, Words(Math.Max(rightWords, 1), 3u));
        using var result = new GpuBigUInt(_workspace, Math.Max(resultWords, 1));

        Action call = operation switch
        {
            "Add" => () => GpuBigIntMath.Add(left, right, result),
            "Sub" => () => GpuBigIntMath.Sub(left, right, result),
            "BitwiseAnd" => () => GpuBigIntMath.BitwiseAnd(left, right, result),
            "BitwiseOr" => () => GpuBigIntMath.BitwiseOr(left, right, result),
            "BitwiseXor" => () => GpuBigIntMath.BitwiseXor(left, right, result),
            "Gcd" => () => GpuBigIntMath.Gcd(left, right, result),
            "ModInverse" => () => GpuBigIntMath.ModInverse(left, right, result),
            "DivideByUInt32" => () => GpuBigIntMath.DivideByUInt32(left, 3u, result),
            "ShiftLeft" => () => GpuBigIntMath.ShiftLeft(left, 3, result),
            "ShiftRight" => () => GpuBigIntMath.ShiftRight(left, 3, result),
            "BitwiseNot" => () => GpuBigIntMath.BitwiseNot(left, result),
            "IsLessThan" => () => GpuBigIntMath.IsLessThan(left, right),
            "Mul" => () => GpuBigIntMath.Mul(left, right, result),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
        };

        Assert.Throws<ArgumentException>(call);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(5, 3)]
    [InlineData(-5, -3)]
    [InlineData(5, -3)]
    [InlineData(-5, 3)]
    [InlineData(3, -5)]
    [InlineData(-3, 5)]
    public void AddSignedMagnitude_SignTable_MatchesBigIntegerSum(int leftValue, int rightValue)
    {
        // The magnitude and the returned sign are those of the BigInteger sum. Equal magnitudes with opposite signs
        // are left out: the sign of a zero sum is #651.
        // mutant: GpuBigIntMath.cs:82 `return rightNegative` -> `return !rightNegative` / `leftNegative` (3 + -5, -3 + 5)
        using var left = new GpuBigUInt(_workspace, Words(2, (uint)Math.Abs(leftValue)));
        using var right = new GpuBigUInt(_workspace, Words(2, (uint)Math.Abs(rightValue)));
        using var result = new GpuBigUInt(_workspace, 2);

        var negative = GpuBigIntMath.AddSignedMagnitude(left, leftValue < 0, right, rightValue < 0, result);

        var sum = leftValue + rightValue;
        Assert.Equal((new BigInteger(Math.Abs(sum)), sum < 0), (result.ToBigInteger(), negative));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(40)]
    [InlineData(-40)]
    [InlineData(64)]
    public void ShiftAndResize_AliasedSourceAndResult_MatchesBigIntegerShift(int shift)
    {
        // Shifting a value in place: 2048 words take two passes of a 1024-thread block, and the second pass of a left
        // shift reads words that the first pass writes, so an in-place kernel would corrupt the result.
        // mutant: GpuBigIntMath.cs:260 `==` -> `!=` (the kernel runs in place)
        // mutant: GpuBigIntMath.cs:263 / :264 temporary copy or launch removed
        // mutant: GpuBigIntMath.cs:279 `return` removed (the host path shifts a second time)
        const int Words = 2048;
        var value = RandomValue(seed: 598, Words);
        using var buffer = GpuBigUInt.FromBigInteger(_workspace, value, Words);

        GpuBigIntMath.ShiftAndResize(buffer, shift, buffer);

        Assert.Equal(Shifted(value, shift, Words), buffer.ToBigInteger());
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(40, 8)]
    [InlineData(-40, 8)]
    [InlineData(0, 8)]
    [InlineData(64, 8)]
    [InlineData(-64, 8)]
    [InlineData(40, 4)]
    [InlineData(-40, 12)]
    public void ShiftAndResize_CpuFallback_MatchesBigIntegerShift(int shift, int resultWords)
    {
        // The host path shifts left for a positive amount and right for a negative one, truncated or zero-extended to
        // the result size.
        // mutant: GpuBigIntMath.cs:286 copy removed; :288 `val << signedBitShift` -> `val >> signedBitShift`,
        //         `val >> -signedBitShift` -> `val << -signedBitShift` / `val >> +signedBitShift`
        // mutant: GpuBigIntMath.cs:289 / :290 WriteLimbs or upload removed
        // ForceCpuFallback is AsyncLocal, so the override cannot leak into other tests.
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(64);
            const int SourceWords = 8;
            var value = RandomValue(seed: 599, SourceWords);
            using var source = GpuBigUInt.FromBigInteger(workspace, value, SourceWords);
            using var result = new GpuBigUInt(workspace, resultWords);

            // FromBigInteger returns its staging array to ArrayPool with the value in it, and the host path rents an array
            // of the same size: put a different pattern there, so the shift must read the source from the device.
            var stale = System.Buffers.ArrayPool<uint>.Shared.Rent(SourceWords);
            Array.Fill(stale, 0xDEAD_BEEFu);
            System.Buffers.ArrayPool<uint>.Shared.Return(stale);

            GpuBigIntMath.ShiftAndResize(source, shift, result);

            Assert.Equal(Shifted(value, shift, resultWords), result.ToBigInteger());
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NormalizeToMsb_ZeroSource_ClearsPrefilledResult()
    {
        // A zero source has no top bit: the result is cleared and the shift and bit count are 0.
        // mutant: GpuBigIntMath.cs:313 `ClearMemoryAsync(resultBuffer...)` removed
        using var source = new GpuBigUInt(_workspace, 4);
        using var result = new GpuBigUInt(_workspace, Enumerable.Repeat(uint.MaxValue, 6).ToArray());

        var shift = GpuBigIntMath.NormalizeToMsb(source, result, out var actualBits);

        Assert.Equal((0, 0, BigInteger.Zero), (shift, actualBits, result.ToBigInteger()));
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(10)]
    [InlineData(5000)]
    [InlineData(12345)]
    public void Pow10_ResultHasMinimalWords(int exponent)
    {
        // 10^e is exact and trimmed to the words its bit length needs.
        // mutant: GpuBigIntMath.cs:467 `(actualBits + 31) / 32` -> `(actualBits + 31) * 32`
        var expected = BigInteger.Pow(10, exponent);
        using var power = GpuBigIntMath.Pow10(_workspace, exponent);

        Assert.Equal((expected, (int)((expected.GetBitLength() + 31) / 32)), (power.ToBigInteger(), power.NumWords));
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Pow10_MaxExponent_IsAccepted()
    {
        // The largest documented exponent is accepted; one more is rejected. 10^50,000,000 has about 5.2 million words
        // (under a second on an RTX 3070 Ti); the value is checked by its bit length (log2(10) * e, rounded up).
        // mutant: GpuBigIntMath.cs:403 `exponent > MaxPow10Exponent` -> `exponent >= MaxPow10Exponent`
        const int MaxExponent = 50_000_000;
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuBigIntMath.Pow10(_workspace, MaxExponent + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuBigIntMath.Pow10(_workspace, -1));

        using var power = GpuBigIntMath.Pow10(_workspace, MaxExponent);

        var expectedBits = (int)Math.Ceiling(MaxExponent * Math.Log2(10));
        Assert.Equal(expectedBits, power.GetBitLength());
    }

    private static uint[] Words(int count, uint low)
    {
        var words = new uint[count];
        words[0] = low;
        return words;
    }

    private static BigInteger RandomValue(int seed, int words)
    {
        var random = new Random(seed);
        var bytes = new byte[words * 4];
        random.NextBytes(bytes);
        bytes[^1] |= 0x80;
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    private static BigInteger Shifted(BigInteger value, int shift, int resultWords)
    {
        var shifted = shift >= 0 ? value << shift : value >> -shift;
        return shifted % (BigInteger.One << (32 * resultWords));
    }
}
