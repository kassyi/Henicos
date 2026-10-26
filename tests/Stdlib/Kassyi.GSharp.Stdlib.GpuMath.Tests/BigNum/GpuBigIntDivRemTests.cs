using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// Properties checked here (property-test-design §1):
// - DivRem / Div / Mod: q * b + r == a and 0 <= r < b (this pair fixes q and r), and agreement with BigInteger.DivRem.
// - Reduce / ReduceZeroSync: the same identity, starting from a reciprocal built on the host so that the correction
//   branches (quotient too small, too large, zero) are reached on purpose.
// - Sqrt: s * s <= a < (s + 1) * (s + 1).
// - Reciprocal / InverseSqrt: the Newton result is floor(exact) or one less (bounds derived above each check).
// - Gcd: the result divides both operands and agrees with BigInteger.GreatestCommonDivisor.
// Every expected value is computed from the words that the test holds, never from a value read back from the GPU.
// The correction loops of GpuBigIntDivRem (CorrectUnderflow / CorrectRemainder) and ThrowIfSizeMismatch carry
// [ExcludeFromCodeCoverage], and the kernels cannot be measured, so the comments on the inputs say which branch each
// input reaches and why.
public class GpuBigIntDivRemTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;

    public GpuBigIntDivRemTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DivRem_Reduce_InverseSqrt_Heron()
    {
        using var customCtx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        uint[] aWords = [4000000000, 4000000000, 4000000000, 4000000000];
        uint[] bWords = [2, 0, 0, 0];
        var a = FromWords(aWords);
        var b = FromWords(bWords);

        using var gpuA = new GpuBigUInt(customCtx, aWords);
        using var gpuB = new GpuBigUInt(customCtx, bWords);
        using var q = new GpuBigUInt(customCtx, 4);
        using var r = new GpuBigUInt(customCtx, 4);

        // Divide (b = 2 takes the single-word power-of-two path).
        GpuBigIntDivRem.DivRem(gpuA, gpuB, q, r);
        AssertQuotientAndRemainder(a, b, q.ToBigInteger(), r.ToBigInteger());

        // Reciprocal: a has 128 bits, so 2^256 / a keeps 128 bits; the buffer holds (256 + 31) / 32 + 1 words.
        const int ReciprocalBits = 256;
        using var recip = new GpuBigUInt(customCtx, ReciprocalBits / 32 + 1);
        GpuBigIntDivRem.Reciprocal(gpuA, recip, ReciprocalBits);
        AssertReciprocalBound(a, ReciprocalBits, recip.ToBigInteger());

        // InverseSqrt: floor(2^(2 * 128) / a) under the square root, with no margin words.
        using var invSqrt = new GpuBigUInt(customCtx, 4);
        GpuBigIntDivRem.InverseSqrt(gpuA, invSqrt, 0);
        AssertInverseSqrtBound(a, 4 * 32, invSqrt.ToBigInteger());

        // Sqrt (Heron refinement after the inverse square root).
        using var sqrt = new GpuBigUInt(customCtx, 4);
        GpuBigIntDivRem.Sqrt(gpuA, sqrt);
        AssertFloorSquareRoot(a, sqrt.ToBigInteger());
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_DivRem_MegaDivision_4096Words_MatchesCpuReference()
    {
        const int NumWords = 4096; // 131,072 bits
        using var workspace = new GpuWorkspace(NumWords * 4, GpuExecutionEngine.GSharpAot);

        var rnd = new Random(42);
        var leftData = new uint[NumWords];
        var rightData = new uint[NumWords];
        for (var i = 0; i < NumWords; i++)
        {
            leftData[i] = (uint)rnd.Next();
            rightData[i] = (uint)rnd.Next();
        }

        leftData[^1] |= 0x80000000u;
        rightData[^1] |= 0x80000000u;
        // Make dividend larger than divisor at the top word so quotient is non-zero
        leftData[^1] |= 0xFFFF0000u;
        rightData[^1] &= 0x8000FFFFu;

        // The expected values come from the host words, not from the GPU buffers.
        var expectedA = FromWords(leftData);
        var expectedB = FromWords(rightData);

        using var gpuA = new GpuBigUInt(workspace, leftData);
        using var gpuB = new GpuBigUInt(workspace, rightData);
        using var gpuQ = new GpuBigUInt(workspace, NumWords);
        using var gpuR = new GpuBigUInt(workspace, NumWords);

        GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);
        AssertQuotientAndRemainder(expectedA, expectedB, gpuQ.ToBigInteger(), gpuR.ToBigInteger());

        using var gpuSqrt = new GpuBigUInt(workspace, NumWords);
        GpuBigIntDivRem.Sqrt(gpuA, gpuSqrt);
        AssertFloorSquareRoot(expectedA, gpuSqrt.ToBigInteger());

        // Two paths agree: GpuBigFloat.Divide on the kernel provider (shiftedLeft has 3 * NumWords = 12,288 words,
        // rightOperand has 4,096 words) and on the CPU fallback, both reading the same operands.
        var execCtx = new GpuBigIntExecutionContext(workspace, workspace.MemoryManager, workspace.Stream);
        var floatProvider = GSharpKernelFloatProvider.Instance;
        using var floatResult = new GpuBigUInt(workspace, NumWords);
        var leftOp = new GpuFloatOperand(gpuA.Handle, 1000);
        var rightOp = new GpuFloatOperand(gpuB.Handle, 1000);
        var floatRes = floatProvider.Divide(ref execCtx, in leftOp, in rightOp, floatResult.Handle);

        var cpuFloatProvider = CSharpCpuBigFloatArithmetic.Instance;
        using var cpuResult = new GpuBigUInt(workspace, NumWords);
        var expectedFloatRes = cpuFloatProvider.Divide(ref execCtx, in leftOp, in rightOp, cpuResult.Handle);

        Assert.Equal(expectedFloatRes.Exponent, floatRes.Exponent);
        Assert.Equal(expectedFloatRes.IsNegative, floatRes.IsNegative);
        Assert.Equal(cpuResult.ToBigInteger(), floatResult.ToBigInteger());
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2048, 1024)]
    [InlineData(4096, 2048)]
    [InlineData(4096, 4095)]
    public void Test_DivRem_MegaDivision_ShiftedDivisorBoundary_MatchesCpuReference(int numWords, int divisorActiveWords)
    {
        RunShiftedDivisor(numWords, divisorActiveWords, seed: 1337 + numWords + divisorActiveWords);
    }

    // The same shape as the Stress test above at sizes around the warp (32) and block (1024) sizes, so that the
    // normal run crosses the 1D / 2D NTT switch (transform length 512) and the multi-block reciprocal grid.
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(31, 16)]
    [InlineData(32, 16)]
    [InlineData(33, 17)]
    [InlineData(64, 63)]
    [InlineData(1023, 512)]
    [InlineData(1024, 512)]
    [InlineData(1025, 513)]
    [InlineData(1024, 1023)]
    public void DivRem_ShiftedDivisor_AroundWarpAndBlockSizes_SatisfiesDivisionIdentity(int numWords, int divisorActiveWords)
    {
        RunShiftedDivisor(numWords, divisorActiveWords, seed: 20261026 + numWords * 31 + divisorActiveWords);
    }

    private static void RunShiftedDivisor(int numWords, int divisorActiveWords, int seed)
    {
        using var workspace = new GpuWorkspace(numWords * 4, GpuExecutionEngine.GSharpAot);

        var rnd = new Random(seed);
        var leftData = new uint[numWords];
        var rightData = new uint[numWords];
        for (var i = 0; i < numWords; i++)
        {
            leftData[i] = (uint)rnd.Next();
        }

        for (var i = 0; i < divisorActiveWords; i++)
        {
            rightData[i] = (uint)rnd.Next();
        }

        leftData[^1] |= 0x80000000u;
        rightData[divisorActiveWords - 1] |= 0x00010000u;

        var expectedA = FromWords(leftData);
        var expectedB = FromWords(rightData);

        using var gpuA = new GpuBigUInt(workspace, leftData);
        using var gpuB = new GpuBigUInt(workspace, rightData);
        using var gpuQ = new GpuBigUInt(workspace, numWords);
        using var gpuR = new GpuBigUInt(workspace, numWords);

        GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);

        AssertQuotientAndRemainder(expectedA, expectedB, gpuQ.ToBigInteger(), gpuR.ToBigInteger());
    }

    public static TheoryData<string> DivRemBoundaryInputs() =>
    [
        "ZeroDividend",
        "DividendShorterThanDivisor",
        "DividendJustBelowDivisor",
        "DividendEqualsDivisor",
        "DivisorOne",
        "DivisorPowerOfTwoBelowWord",
        "DivisorThree",
        "DivisorMaxWord",
        "DivisorPowerOfTwoWordAligned",
        "DivisorPowerOfTwoUnaligned",
        "DivisorPowerOfTwoInTopWord",
        "DivisorTopWordPowerOfTwoWithLowBits",
        "NormalizationShiftZero",
        "AllOnesDividend",
        "ExactMultiple",
        "RemainderIsDivisorMinusOne"
    ];

    // Each named input reaches one branch of GpuBigIntDivRem.DivRem / TryDivRemSmallOrPowerOfTwo (lines of
    // src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs):
    // - ZeroDividend, DividendShorterThanDivisor: a has fewer bits than b (line 105, q = 0 and r = a).
    // - DividendJustBelowDivisor: a = b - 1 has as many bits as b, so it runs the reciprocal path with q = 0.
    // - DivisorOne (line 148), DivisorPowerOfTwoBelowWord (line 156), DivisorThree / DivisorMaxWord (DivideByUInt32,
    //   line 181): divisors of 32 bits or less.
    // - DivisorPowerOfTwoWordAligned (2^64, remBits == 0), DivisorPowerOfTwoUnaligned (2^47, remBits != 0),
    //   DivisorPowerOfTwoInTopWord (2^229): the multi-word power-of-two path (line 205).
    // - DivisorTopWordPowerOfTwoWithLowBits (2^64 + 1): passes the top-word check (line 207) and is rejected by the
    //   mask check (line 216), then runs the reciprocal path.
    // - NormalizationShiftZero: the divisor fills its buffer with the top bit set, so lz == 0 (line 117).
    // - AllOnesDividend, ExactMultiple (r == 0), RemainderIsDivisorMinusOne (r == b - 1): the reciprocal path at the
    //   carries and at both ends of the remainder range.
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(DivRemBoundaryInputs))]
    public void DivRem_NamedBoundaryInputs_SatisfyDivisionIdentityAndMatchHostReference(string input)
    {
        const int NumWords = 8;
        var (dividend, divisor) = BuildDivRemInput(input, NumWords);
        using var workspace = new GpuWorkspace(NumWords * 4, GpuExecutionEngine.GSharpAot);

        using var gpuA = new GpuBigUInt(workspace, ToWords(dividend, NumWords));
        using var gpuB = new GpuBigUInt(workspace, ToWords(divisor, NumWords));
        using var gpuQ = new GpuBigUInt(workspace, NumWords);
        using var gpuR = new GpuBigUInt(workspace, NumWords);

        GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);

        AssertQuotientAndRemainder(dividend, divisor, gpuQ.ToBigInteger(), gpuR.ToBigInteger());
    }

    private static (BigInteger Dividend, BigInteger Divisor) BuildDivRemInput(string input, int numWords)
    {
        var allOnes = (BigInteger.One << (numWords * 32)) - 1;
        var random = FromWords(RandomWords(seed: 101, numWords, topBit: true));
        var fourWordDivisor = FromWords(RandomWords(seed: 102, 4, topBit: true));
        var threeWordDivisor = FromWords(RandomWords(seed: 103, 3, topBit: true));
        var multiplier = FromWords(RandomWords(seed: 104, 4, topBit: true));
        return input switch
        {
            "ZeroDividend" => (BigInteger.Zero, FromWords(RandomWords(seed: 105, 2, topBit: true))),
            "DividendShorterThanDivisor" => (FromWords(RandomWords(seed: 106, 2, topBit: true)), fourWordDivisor),
            "DividendJustBelowDivisor" => (fourWordDivisor - 1, fourWordDivisor),
            "DividendEqualsDivisor" => (fourWordDivisor, fourWordDivisor),
            "DivisorOne" => (random, BigInteger.One),
            "DivisorPowerOfTwoBelowWord" => (allOnes, BigInteger.One << 31),
            "DivisorThree" => (random, new BigInteger(3)),
            "DivisorMaxWord" => (allOnes, new BigInteger(uint.MaxValue)),
            "DivisorPowerOfTwoWordAligned" => (random, BigInteger.One << 64),
            "DivisorPowerOfTwoUnaligned" => (random, BigInteger.One << 47),
            "DivisorPowerOfTwoInTopWord" => (allOnes, BigInteger.One << (32 * (numWords - 1) + 5)),
            "DivisorTopWordPowerOfTwoWithLowBits" => (random, (BigInteger.One << 64) + 1),
            "NormalizationShiftZero" => (allOnes, FromWords(RandomWords(seed: 107, numWords, topBit: true))),
            "AllOnesDividend" => (allOnes, threeWordDivisor),
            "ExactMultiple" => (threeWordDivisor * multiplier, threeWordDivisor),
            "RemainderIsDivisorMinusOne" => (threeWordDivisor * multiplier + threeWordDivisor - 1, threeWordDivisor),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown DivRem input.")
        };
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(1)]
    [InlineData(33)]
    [InlineData(1025)]
    public void DivRem_ZeroDivisor_ThrowsDivideByZeroException(int numWords)
    {
        using var workspace = new GpuWorkspace(numWords * 2, GpuExecutionEngine.GSharpAot);

        var leftData = new uint[numWords];
        leftData[^1] = 0x80000000u;

        using var gpuA = new GpuBigUInt(workspace, leftData);
        using var gpuB = new GpuBigUInt(workspace, new uint[numWords]);
        using var gpuQ = new GpuBigUInt(workspace, numWords);
        using var gpuR = new GpuBigUInt(workspace, numWords);

        // A zero divisor is rejected before any kernel runs.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:101 `throw new DivideByZeroException();` -> `return;`
        // input: numWords 1, 33, 1025
        Assert.Throws<DivideByZeroException>(() => GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Div_And_Mod_GeneralDivisor_SatisfyDivisionIdentity()
    {
        const int NumWords = 8;
        using var workspace = new GpuWorkspace(NumWords * 4, GpuExecutionEngine.GSharpAot);

        var aWords = RandomWords(seed: 201, NumWords, topBit: true);
        var bWords = new uint[NumWords];
        RandomWords(seed: 202, 3, topBit: true).CopyTo(bWords, 0);
        var a = FromWords(aWords);
        var b = FromWords(bWords);

        using var gpuA = new GpuBigUInt(workspace, aWords);
        using var gpuB = new GpuBigUInt(workspace, bWords);
        using var gpuQ = new GpuBigUInt(workspace, NumWords);
        using var gpuR = new GpuBigUInt(workspace, NumWords);

        // Div returns the quotient and Mod the remainder of the same division.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:476 `DivRem(leftOperand, rightOperand, resultBuffer, tempR)` -> `DivRem(leftOperand, rightOperand, tempR, resultBuffer)`
        GpuBigIntDivRem.Div(gpuA, gpuB, gpuQ);
        GpuBigIntDivRem.Mod(gpuA, gpuB, gpuR);
        AssertQuotientAndRemainder(a, b, gpuQ.ToBigInteger(), gpuR.ToBigInteger());
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void Div_And_Mod_MismatchedBufferSizes_ThrowArgumentException()
    {
        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);

        using var gpuA = new GpuBigUInt(workspace, RandomWords(seed: 211, 4, topBit: true));
        using var gpuB = new GpuBigUInt(workspace, [7u, 0u, 0u, 0u]);
        // A result buffer one word longer than the operands: Div and Mod require equal sizes.
        using var widerResult = new GpuBigUInt(workspace, 5);

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:474 `ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);` -> removed
        Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.Div(gpuA, gpuB, widerResult));
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:490 `ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);` -> removed
        Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.Mod(gpuA, gpuB, widerResult));
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void Div_And_Mod_ZeroDivisor_ThrowDivideByZeroException()
    {
        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);

        using var gpuA = new GpuBigUInt(workspace, RandomWords(seed: 221, 4, topBit: true));
        using var gpuZero = new GpuBigUInt(workspace, new uint[4]);
        using var result = new GpuBigUInt(workspace, 4);

        // Div and Mod pass the zero divisor on to DivRem, which rejects it.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:101 `throw new DivideByZeroException();` -> `return;`
        Assert.Throws<DivideByZeroException>(() => GpuBigIntDivRem.Div(gpuA, gpuZero, result));
        Assert.Throws<DivideByZeroException>(() => GpuBigIntDivRem.Mod(gpuA, gpuZero, result));
    }

    public static TheoryData<string> ReduceReciprocalInputs() =>
    [
        "ExactReciprocal",
        "QuotientOneTooSmall",
        "QuotientZero",
        "QuotientOneTooLarge",
        "QuotientThreeTooLarge"
    ];

    // Reduce estimates q = floor(a * x / 2^T) from a reciprocal x and corrects it. The reciprocal is built on the host
    // so that the estimate is known (CorrectUnderflow / CorrectRemainder in GpuBigIntDivRem.cs are excluded from
    // coverage, so the estimate is the witness):
    // - ExactReciprocal: x = floor(2^T / b), the estimate is q or q - 1.
    // - QuotientOneTooSmall: estimate q - 1, so r = R + b < 2b and CorrectRemainder subtracts b once (line 447).
    // - QuotientZero: x = 0, so r = a has more bits than b + 1 and CorrectRemainder takes the shifted branch (line 426).
    // - QuotientOneTooLarge / QuotientThreeTooLarge: the estimate overshoots, a - q * b is negative and CorrectUnderflow
    //   adds b back once / three times (line 297; the add-back step of the division).
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ReduceReciprocalInputs))]
    public void Reduce_HandBuiltReciprocal_CorrectsQuotientAndRemainder(string input)
    {
        var reduce = BuildReduceInput(input);
        using var workspace = new GpuWorkspace(reduce.ReciprocalWords * 4, GpuExecutionEngine.GSharpAot);

        using var gpuA = new GpuBigUInt(workspace, ToWords(reduce.Dividend, ReduceDividendWords));
        using var gpuB = new GpuBigUInt(workspace, ToWords(reduce.Divisor, ReduceDivisorWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(reduce.Reciprocal, reduce.ReciprocalWords));
        using var gpuQ = new GpuBigUInt(workspace, ReduceDividendWords);
        using var gpuR = new GpuBigUInt(workspace, ReduceDividendWords);

        GpuBigIntDivRem.Reduce(gpuA, gpuB, gpuX, reduce.TargetBits, ReduceDividendWords, gpuQ, gpuR);

        AssertQuotientAndRemainder(reduce.Dividend, reduce.Divisor, gpuQ.ToBigInteger(), gpuR.ToBigInteger());
    }

    public static TheoryData<string> ReduceZeroSyncReciprocalInputs() =>
    [
        "ExactReciprocal",
        "QuotientOneTooSmall",
        "QuotientOneTooLarge"
    ];

    // ReduceZeroSync corrects the remainder inside BarrettCorrectionKernel (subtract b while non-negative, add b back
    // once it underflows; add b while negative). The inputs cover a correct estimate, one subtraction and one add-back.
    // The quotient is not checked: the kernel corrects only the remainder and leaves the estimate in the quotient
    // buffer (QuotientOneTooSmall / QuotientOneTooLarge return q - 1 / q + 1). Whether that is the contract is open.
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ReduceZeroSyncReciprocalInputs))]
    public void ReduceZeroSync_HandBuiltReciprocal_CorrectsRemainder(string input)
    {
        var reduce = BuildReduceInput(input);
        using var workspace = new GpuWorkspace(reduce.ReciprocalWords * 4, GpuExecutionEngine.GSharpAot);

        using var gpuA = new GpuBigUInt(workspace, ToWords(reduce.Dividend, ReduceDividendWords));
        using var gpuB = new GpuBigUInt(workspace, ToWords(reduce.Divisor, ReduceDivisorWords));
        using var gpuX = new GpuBigUInt(workspace, ToWords(reduce.Reciprocal, reduce.ReciprocalWords));
        using var gpuQ = new GpuBigUInt(workspace, ReduceDividendWords);
        using var gpuR = new GpuBigUInt(workspace, ReduceDividendWords);

        GpuBigIntDivRem.ReduceZeroSync(gpuA, gpuB, gpuX, reduce.TargetBits, ReduceDividendWords, gpuQ, gpuR);

        // r == a mod b, computed on the host from the words the test holds.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:365 `Math.Min(n, evalWords)` -> `Math.Min(n - 1, evalWords)`
        // input: ExactReciprocal, QuotientOneTooSmall, QuotientOneTooLarge
        Assert.Equal(reduce.Dividend % reduce.Divisor, gpuR.ToBigInteger());
    }

    private const int ReduceDividendWords = 4;
    private const int ReduceDivisorWords = 2;

    private static (BigInteger Dividend, BigInteger Divisor, BigInteger Reciprocal, int TargetBits, int ReciprocalWords)
        BuildReduceInput(string input)
    {
        var a = FromWords(RandomWords(seed: 301, ReduceDividendWords, topBit: true));
        var b = FromWords(RandomWords(seed: 302, ReduceDivisorWords, topBit: true));
        // Same precision as DivRem: n * 32 + aWords * 32 + 512 bits, a multiple of 32 (Reduce shifts by whole words).
        const int TargetBits = ReduceDivisorWords * 32 + ReduceDividendWords * 32 + 512;
        var twoToT = BigInteger.One << TargetBits;
        var q = a / b;

        // x = ceil(target * 2^T / a) gives floor(a * x / 2^T) == target, because a < 2^T.
        BigInteger ReciprocalFor(BigInteger target) => (target * twoToT + a - 1) / a;

        var (x, estimate) = input switch
        {
            "ExactReciprocal" => (twoToT / b, (BigInteger?)null),
            "QuotientOneTooSmall" => (ReciprocalFor(q - 1), q - 1),
            "QuotientZero" => (BigInteger.Zero, BigInteger.Zero),
            "QuotientOneTooLarge" => (ReciprocalFor(q + 1), q + 1),
            "QuotientThreeTooLarge" => (ReciprocalFor(q + 3), q + 3),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown Reduce input.")
        };

        // Witness: the hand-built reciprocal produces the intended quotient estimate.
        if (estimate is { } expectedEstimate)
        {
            Assert.Equal(expectedEstimate, a * x / twoToT);
        }

        return (a, b, x, TargetBits, TargetBits / 32 + 2);
    }

    public static TheoryData<string> SqrtInputs() =>
    [
        "Zero",
        "One",
        "MaxSingleWord",
        "SmallestTwoWord",
        "PerfectSquare",
        "PerfectSquareMinusOne",
        "NextPerfectSquareMinusOne",
        "AllOnes",
        "EvenLeadingZeros",
        "OddLeadingZeros",
        "Words33"
    ];

    // Zero / One / MaxSingleWord take the single-word path (GpuBigIntDivRem.Sqrt.cs:70); the others run the inverse
    // square root and Heron's refinement. Even / odd leading zeros reach both sides of the lz parity fix (line 93).
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SqrtInputs))]
    public void Sqrt_NamedInputs_ReturnsFloorRoot(string input)
    {
        var root = FromWords(RandomWords(seed: 401, 4, topBit: true));
        var (numWords, value) = input switch
        {
            "Zero" => (4, BigInteger.Zero),
            "One" => (4, BigInteger.One),
            "MaxSingleWord" => (4, new BigInteger(uint.MaxValue)),
            "SmallestTwoWord" => (4, BigInteger.One << 32),
            "PerfectSquare" => (8, root * root),
            "PerfectSquareMinusOne" => (8, root * root - 1),
            "NextPerfectSquareMinusOne" => (8, (root + 1) * (root + 1) - 1),
            "AllOnes" => (16, (BigInteger.One << (16 * 32)) - 1),
            "EvenLeadingZeros" => (8, FromWords(RandomWords(seed: 402, 8, topBit: true)) >> 2),
            "OddLeadingZeros" => (8, FromWords(RandomWords(seed: 403, 8, topBit: true)) >> 3),
            "Words33" => (33, FromWords(RandomWords(seed: 404, 33, topBit: true))),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown Sqrt input.")
        };

        using var workspace = new GpuWorkspace(numWords * 8, GpuExecutionEngine.GSharpAot);
        using var gpuA = new GpuBigUInt(workspace, ToWords(value, numWords));
        using var gpuS = new GpuBigUInt(workspace, numWords);

        GpuBigIntDivRem.Sqrt(gpuA, gpuS);

        AssertFloorSquareRoot(value, gpuS.ToBigInteger());
    }

    public static TheoryData<string> ReciprocalInputs() =>
    [
        "TopBitOnly4Words",
        "AllOnes4Words",
        "Random16WordsDivRemPrecision",
        "Random33Words"
    ];

    // Precondition (all callers in GpuMath): the operand is normalized (top bit of its top word set), and
    // 2^T >= 4 * b. TopBitOnly makes 2^T / b an exact power of two.
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ReciprocalInputs))]
    public void Reciprocal_NormalizedOperand_IsFloorOrOneBelow(string input)
    {
        var (numWords, targetBits) = input switch
        {
            "TopBitOnly4Words" or "AllOnes4Words" => (4, 256),
            "Random16WordsDivRemPrecision" => (16, 16 * 32 + 16 * 32 + 512),
            "Random33Words" => (33, 2 * 33 * 32),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown Reciprocal input.")
        };
        var b = input switch
        {
            "TopBitOnly4Words" => BigInteger.One << (numWords * 32 - 1),
            "AllOnes4Words" => (BigInteger.One << (numWords * 32)) - 1,
            _ => FromWords(RandomWords(seed: 500 + numWords, numWords, topBit: true))
        };

        var resultWords = targetBits / 32 + 1;
        using var workspace = new GpuWorkspace(resultWords * 4, GpuExecutionEngine.GSharpAot);
        using var gpuB = new GpuBigUInt(workspace, ToWords(b, numWords));
        using var gpuX = new GpuBigUInt(workspace, resultWords);

        GpuBigIntDivRem.Reciprocal(gpuB, gpuX, targetBits);

        AssertReciprocalBound(b, targetBits, gpuX.ToBigInteger());
    }

    public static TheoryData<string> InverseSqrtInputs() =>
    [
        "PowerOfFour4Words",
        "TopBitOnly4Words",
        "AllOnes4Words",
        "Random16WordsMargin16",
        "SecondBitTop33WordsMargin16"
    ];

    // Precondition (Sqrt and GSharpKernelFloatProvider): the operand is normalized with an even shift, so its bit
    // length is 32n or 32n - 1. PowerOfFour makes the exact root a power of two.
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(InverseSqrtInputs))]
    public void InverseSqrt_NormalizedOperand_IsFloorOrOneBelow(string input)
    {
        var (numWords, marginWords, a) = input switch
        {
            "PowerOfFour4Words" => (4, 0, BigInteger.One << (4 * 32 - 2)),
            "TopBitOnly4Words" => (4, 0, BigInteger.One << (4 * 32 - 1)),
            "AllOnes4Words" => (4, 0, (BigInteger.One << (4 * 32)) - 1),
            "Random16WordsMargin16" => (16, 16, FromWords(RandomWords(seed: 601, 16, topBit: true))),
            "SecondBitTop33WordsMargin16" => (33, 16, FromWords(RandomWords(seed: 602, 33, topBit: true)) >> 1),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown InverseSqrt input.")
        };

        using var workspace = new GpuWorkspace((numWords + marginWords) * 8, GpuExecutionEngine.GSharpAot);
        using var gpuA = new GpuBigUInt(workspace, ToWords(a, numWords));
        using var gpuX = new GpuBigUInt(workspace, numWords + marginWords);

        GpuBigIntDivRem.InverseSqrt(gpuA, gpuX, marginWords);

        AssertInverseSqrtBound(a, (numWords + marginWords) * 32, gpuX.ToBigInteger());
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void Reciprocal_And_InverseSqrt_InvalidArguments_Throw()
    {
        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        using var zero = new GpuBigUInt(workspace, new uint[4]);
        using var normalized = new GpuBigUInt(workspace, RandomWords(seed: 701, 4, topBit: true));
        using var reciprocal = new GpuBigUInt(workspace, 9);
        using var exactSize = new GpuBigUInt(workspace, 4);
        using var oneWordTooLong = new GpuBigUInt(workspace, 5);

        // The reciprocal of zero does not exist.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:502 `throw new DivideByZeroException();` -> `return BigInteger.One;`
        Assert.Throws<DivideByZeroException>(() => GpuBigIntDivRem.Reciprocal(zero, reciprocal, 256));

        // Nor does the inverse square root of zero.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.Sqrt.cs:16 `throw new DivideByZeroException();` -> `return;`
        Assert.Throws<DivideByZeroException>(() => GpuBigIntDivRem.InverseSqrt(zero, exactSize, 0));

        // InverseSqrt writes exactly n + marginWords words, so any other size is rejected.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.Sqrt.cs:8 `!=` -> `<`
        Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.InverseSqrt(normalized, oneWordTooLong, 0));
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(65)]
    [InlineData(256)]
    [InlineData(600)]
    [InlineData(1024)]
    public void DivRem_SmallAndBoundaryDivisors_MatchesBigIntegerReference(int numWords)
    {
        using var workspace = new GpuWorkspace(numWords * 4, GpuExecutionEngine.GSharpAot);

        var rnd = new Random(20261025 + numWords);
        var leftData = new uint[numWords];
        for (var i = 0; i < numWords; i++)
        {
            leftData[i] = (uint)rnd.NextInt64(0, 1L << 32);
        }

        leftData[^1] |= 0x80000000u;

        var twoWordDivisor = new uint[numWords];
        twoWordDivisor[0] = (uint)rnd.NextInt64(0, 1L << 32);
        twoWordDivisor[1] = (uint)rnd.NextInt64(0, 1L << 32) | 0x80000000u;

        var halfWordCount = numWords / 2;
        var halfWordDivisor = new uint[numWords];
        for (var i = 0; i < halfWordCount; i++)
        {
            halfWordDivisor[i] = (uint)rnd.NextInt64(0, 1L << 32);
        }

        halfWordDivisor[halfWordCount - 1] |= 0x80000000u;

        var divisors = new BigInteger[]
        {
            BigInteger.One,
            new(2),
            new(3),
            BigInteger.One << 32,
            (BigInteger.One << 33) + BigInteger.One,
            BigInteger.One << 15,
            BigInteger.One << 31,
            BigInteger.One << 47,
            FromWords(twoWordDivisor),
            FromWords(halfWordDivisor)
        };

        using var gpuA = new GpuBigUInt(workspace, leftData);
        using var gpuQ = new GpuBigUInt(workspace, numWords);
        using var gpuR = new GpuBigUInt(workspace, numWords);
        var expectedA = FromWords(leftData);

        foreach (var divisor in divisors)
        {
            using var gpuB = new GpuBigUInt(workspace, ToWords(divisor, numWords));
            GpuBigIntDivRem.DivRem(gpuA, gpuB, gpuQ, gpuR);

            AssertQuotientAndRemainder(expectedA, divisor, gpuQ.ToBigInteger(), gpuR.ToBigInteger());
        }
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(64)]
    [InlineData(256)]
    [InlineData(600)]
    public void Gcd_BoundaryOperandPairs_MatchesBigIntegerReference(int numWords)
    {
        using var workspace = new GpuWorkspace(numWords * 4, GpuExecutionEngine.GSharpAot);

        var rnd = new Random(20261025 + numWords);
        var arrA = new uint[numWords];
        for (var i = 0; i < numWords; i++)
        {
            arrA[i] = (uint)rnd.NextInt64(0, 1L << 32);
        }

        arrA[^1] |= 0x40000000u;
        var bigA = FromWords(arrA);

        // A shared factor g of a quarter of the words, times two cofactors of a third of the words each.
        var sharedFactor = FromWords(RandomWords(seed: 801 + numWords, numWords / 4, topBit: true));
        var cofactorX = FromWords(RandomWords(seed: 802 + numWords, numWords / 3, topBit: true));
        var cofactorY = FromWords(RandomWords(seed: 803 + numWords, numWords / 3, topBit: true));

        var pairs = new (BigInteger Left, BigInteger Right, BigInteger KnownFactor)[]
        {
            (bigA, BigInteger.One, BigInteger.One),
            (BigInteger.One, bigA, BigInteger.One),
            (bigA, bigA + BigInteger.One, BigInteger.One),
            (bigA, bigA, bigA),
            (bigA, BigInteger.Zero, bigA),
            (BigInteger.Zero, bigA, bigA),
            (sharedFactor * cofactorX, sharedFactor * cofactorY, sharedFactor)
        };

        using var gpuResult = new GpuBigUInt(workspace, numWords);
        foreach (var (left, right, knownFactor) in pairs)
        {
            using var gpuLeft = new GpuBigUInt(workspace, ToWords(left, numWords));
            using var gpuRight = new GpuBigUInt(workspace, ToWords(right, numWords));

            GpuBigIntMath.Gcd(gpuLeft, gpuRight, gpuResult);
            var actual = gpuResult.ToBigInteger();

            // g divides both operands, and every common factor that the input planted divides g.
            // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:99 `a.DevicePointer` -> `b.DevicePointer`
            // input: all pairs (the result becomes 0)
            Assert.True(actual.Sign > 0, $"gcd({left}, {right}) returned {actual}");
            Assert.True(left % actual == 0 && right % actual == 0, $"gcd {actual} does not divide both operands");
            Assert.True(actual % knownFactor == 0, $"gcd {actual} is not a multiple of the planted factor {knownFactor}");

            // Same result as BigInteger.GreatestCommonDivisor on the host values.
            Assert.Equal(BigInteger.GreatestCommonDivisor(left, right), actual);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void Gcd_WithOne64Words_CompletesViaFastPath()
    {
        const int NumWords = 64;
        using var workspace = new GpuWorkspace(NumWords * 4, GpuExecutionEngine.GSharpAot);

        var rnd = new Random(20261025);
        var arrA = new uint[NumWords];
        for (var i = 0; i < NumWords; i++)
        {
            arrA[i] = (uint)rnd.NextInt64(0, 1L << 32);
        }

        arrA[^1] |= 0x80000000u;
        var bigA = FromWords(arrA);

        using var gpuA = new GpuBigUInt(workspace, ToWords(bigA, NumWords));
        using var gpuOne = new GpuBigUInt(workspace, ToWords(BigInteger.One, NumWords));
        using var gpuResult = new GpuBigUInt(workspace, NumWords);

        GpuBigIntMath.Gcd(gpuA, gpuOne, gpuResult);

        Assert.Equal(BigInteger.One, gpuResult.ToBigInteger());
    }

    private static void AssertQuotientAndRemainder(BigInteger dividend, BigInteger divisor, BigInteger quotient,
        BigInteger remainder)
    {
        // q * b + r == a: the quotient and the remainder recombine to the dividend.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:451 `ctx.QuotientBuffer.CopyFrom(tempQuot, numWords: ctx.QuotientBuffer.NumWords);` -> removed
        // input: Reduce QuotientOneTooSmall
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:395 `GpuBigIntMath.Sub(ctx.QuotientBuffer, one, tempQuot);` -> `GpuBigIntMath.Add(ctx.QuotientBuffer, one, tempQuot);`
        // input: Reduce QuotientOneTooLarge, QuotientThreeTooLarge
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:440 `CreatePowerOfTwo(ctx.Workspace, ctx.QuotientBuffer.NumWords, shift)` -> `CreatePowerOfTwo(ctx.Workspace, ctx.QuotientBuffer.NumWords, shift + 1)`
        // input: Reduce QuotientZero
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:108 `remainderBuffer.CopyFrom(leftOperand, ...)` -> `remainderBuffer.Clear()`
        // input: DivRem DividendShorterThanDivisor
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:151 `quotientBuffer.CopyFrom(leftOperand, ...)` -> removed
        // input: DivRem DivisorOne
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:158 `var shift = bActualBits - 1;` -> `var shift = bActualBits;`
        // input: DivRem DivisorPowerOfTwoBelowWord
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:197 `WriteWord(remainderBuffer.DevicePointer, 0, rem, ...)` -> `WriteWord(remainderBuffer.DevicePointer, 0, 0u, ...)`
        // input: DivRem DivisorThree
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:236 `Math.Min(nA, fullWords)` -> `Math.Min(nA, fullWords - 1)`
        // input: DivRem DivisorPowerOfTwoWordAligned, DivisorPowerOfTwoUnaligned, DivisorPowerOfTwoInTopWord
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:216 `if (!bAndMask.IsZero())` -> `if (bAndMask.NumWords < 0 && !bAndMask.IsZero())`
        // input: DivRem DivisorTopWordPowerOfTwoWithLowBits
        Assert.Equal(dividend, quotient * divisor + remainder);

        // 0 <= r < b: with the identity above this fixes q and r uniquely.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:415 `while (!GpuBigIntMath.IsLessThan(...))` -> `while (loopCount < 0 && !GpuBigIntMath.IsLessThan(...))`
        // input: Reduce QuotientOneTooSmall, QuotientZero (the identity still holds, the remainder is too large)
        Assert.True(remainder.Sign >= 0 && remainder < divisor, $"remainder {remainder} is not in [0, {divisor})");

        // Same result as BigInteger.DivRem on the host values (redundant with the two checks above, kept as the
        // reference comparison of property-test-design §1).
        var expectedQuotient = BigInteger.DivRem(dividend, divisor, out var expectedRemainder);
        Assert.Equal(expectedQuotient, quotient);
        Assert.Equal(expectedRemainder, remainder);
    }

    private static void AssertFloorSquareRoot(BigInteger value, BigInteger root)
    {
        // s * s <= a < (s + 1) * (s + 1) defines floor(sqrt(a)) without a reference implementation.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.Sqrt.cs:149 `ShiftRight(sumWithDivision, 1, currentRoot)` -> `ShiftRight(sumWithDivision, 2, currentRoot)`
        // input: Sqrt PerfectSquare, AllOnes, Words33
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.Sqrt.cs:77 `(uint)Math.Sqrt(val)` -> `(uint)Math.Sqrt(val) + 1`
        // input: Sqrt One, MaxSingleWord
        Assert.True(root.Sign >= 0 && root * root <= value, $"{root}^2 exceeds {value}");
        Assert.True(value < (root + 1) * (root + 1), $"({root} + 1)^2 does not exceed {value}");
    }

    private static void AssertReciprocalBound(BigInteger divisor, int targetBits, BigInteger reciprocal)
    {
        // Newton step x' = floor(x * (2^(T+1) - b * x) / 2^T) = floor(y - (b / 2^T) * (y - x)^2) with y = 2^T / b.
        // So x' <= floor(y) always, and once |y - x| < sqrt(y) (2^T >= 4b and the iterations converged), x' >= floor(y) - 1.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:319 `var bitTarget = targetBits + 1;` -> `var bitTarget = targetBits;`
        // input: all Reciprocal inputs
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:79 `return iterations + 1;` -> `return iterations - 1;`
        // input: Random16WordsDivRemPrecision (the other inputs still converge with two fewer iterations)
        var floorY = (BigInteger.One << targetBits) / divisor;
        Assert.True(reciprocal <= floorY && reciprocal >= floorY - 1,
            $"reciprocal {reciprocal} is not floor(2^{targetBits} / b) = {floorY} or one less");
    }

    private static void AssertInverseSqrtBound(BigInteger value, int precisionBits, BigInteger inverseRoot)
    {
        // With y = 2^B / sqrt(a) (B = (n + marginWords) * 32) and x = y * (1 - e), the Newton step
        // x' = floor(x * (3 * 2^(2B) - a * x^2) / 2^(2B+1)) = floor(y - y * (3e^2 - e^3) / 2), so x' <= floor(y) for
        // -1 < e < 3, and x' >= floor(y) - 1 once (3/2) * (y - x)^2 / y < 1.
        // floor(y) = floor(sqrt(floor(2^(2B) / a))), the value the CPU provider returns.
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:190 `hostConstant3[(bit2B + 1) / 32] |= 1u << ((bit2B + 1) % 32);` -> removed
        // input: all InverseSqrt inputs and Test_DivRem_Reduce_InverseSqrt_Heron
        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/GpuBigIntDivRem.cs:79 `return iterations + 1;` -> `return iterations - 1;`
        // input: SecondBitTop33WordsMargin16
        var floorY = FloorSqrt((BigInteger.One << (2 * precisionBits)) / value);
        Assert.True(inverseRoot <= floorY && inverseRoot >= floorY - 1,
            $"inverse square root {inverseRoot} is not floor(2^{precisionBits} / sqrt(a)) = {floorY} or one less");
    }

    // Test-local integer square root (not BigIntExtensions.Sqrt from the code under test); it checks its own result.
    private static BigInteger FloorSqrt(BigInteger value)
    {
        if (value < 2)
        {
            return value;
        }

        var x = BigInteger.One << (int)((value.GetBitLength() + 1) / 2);
        while (true)
        {
            var next = (x + value / x) >> 1;
            if (next >= x)
            {
                break;
            }

            x = next;
        }

        Assert.True(x * x <= value && value < (x + 1) * (x + 1), "test-local FloorSqrt is wrong");
        return x;
    }

    // Test-local conversions so that no expected value goes through BigIntExtensions or GpuBigUInt.
    private static BigInteger FromWords(ReadOnlySpan<uint> words) =>
        new(MemoryMarshal.AsBytes(words), isUnsigned: true, isBigEndian: false);

    private static uint[] ToWords(BigInteger value, int numWords)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        if (value.IsZero)
        {
            return new uint[numWords];
        }

        if (bytes.Length > numWords * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"The test input needs more than {numWords} words.");
        }

        var words = new uint[numWords];
        Buffer.BlockCopy(bytes, 0, words, 0, bytes.Length);
        return words;
    }

    private static uint[] RandomWords(int seed, int count, bool topBit)
    {
        var rnd = new Random(seed);
        var words = new uint[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = (uint)rnd.NextInt64(0, 1L << 32);
        }

        if (topBit)
        {
            words[^1] |= 0x80000000u;
        }

        return words;
    }
}
