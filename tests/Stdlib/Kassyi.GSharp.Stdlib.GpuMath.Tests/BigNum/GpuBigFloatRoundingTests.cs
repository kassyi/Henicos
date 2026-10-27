#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Rounding contract of the big-float providers (#661): <see cref="GSharpKernelFloatProvider"/> (GPU) and
/// <see cref="CSharpCpuBigFloatArithmetic"/> (CPU) return <c>RZ_p</c> of the exact value, the exact value rounded toward
/// zero to <c>p = 32·n</c> bits for <c>n</c> result words, and throw <see cref="OverflowException"/> when the exponent
/// of the result does not fit in <see cref="int"/>.
/// </summary>
/// <remarks>
/// Every expected value comes from the operands that the test holds (words, exponent, sign) through
/// <see cref="BigFloatReference"/>, never from a value read back from the GPU. Each case runs on both engines, so the two
/// providers return the same bits.
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class GpuBigFloatRoundingTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBigFloatRoundingTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    // Normalize moves the top bit of the significand to the top of the result and changes the exponent by the same
    // shift; an exponent outside int throws OverflowException instead of wrapping around (#661 §4.10). The last value
    // on each side that fits is returned exactly.
    // mutant: GSharpKernelFloatProvider.cs:62 `checked(value.Exponent - shift)` -> `unchecked(...)` (Gpu)
    // mutant: CSharpCpuBigFloatArithmetic.cs:92 `checked(value.Exponent - shift)` -> `unchecked(...)` (Cpu)
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Gpu")]
    [InlineData("Cpu")]
    public void Normalize_ExponentOutOfRange_ThrowsOverflow(string engine)
    {
        var arithmetic = Arithmetic(engine);

        // 1 in one word normalized to one word: the shift is +31, so the exponent goes down by 31.
        Assert.Throws<OverflowException>(() => Normalize(arithmetic, [1u], int.MinValue + 30, 1));
        var lowest = Normalize(arithmetic, [1u], int.MinValue + 31, 1);
        Assert.Equal((BigInteger.One << 31, int.MinValue, false), (lowest.Significand, lowest.Result.Exponent, lowest.Result.IsNegative));

        // A 3-word significand with its top bit set normalized to one word: the shift is -64, so the exponent goes up
        // by 64 and the low words are dropped.
        uint[] wide = [5u, 0u, 0x8000_0000u];
        Assert.Throws<OverflowException>(() => Normalize(arithmetic, wide, int.MaxValue - 63, 1));
        var highest = Normalize(arithmetic, wide, int.MaxValue - 64, 1);
        Assert.Equal((new BigInteger(0x8000_0000u), int.MaxValue, false), (highest.Significand, highest.Result.Exponent, highest.Result.IsNegative));
    }

    private enum FloatOperation
    {
        Add,
        Multiply,
        Divide,
        Reciprocal,
    }

    /// <summary>
    /// An operand <c>±Significand·2^Exponent</c> stored in exactly <c>Words.Length</c> words (leading zero words kept).
    /// </summary>
    private sealed record Operand(uint[] Words, int Exponent, bool IsNegative = false)
    {
        public BigInteger Significand => FromWords(Words);
    }

    private sealed record ArithmeticCase(FloatOperation Operation, Operand Left, Operand? Right, int Words);

    private static readonly Dictionary<string, ArithmeticCase> s_cases = CreateCases();

    public static TheoryData<string, string> CasesOnBothEngines()
    {
        var data = new TheoryData<string, string>();
        foreach (var name in s_cases.Keys)
        {
            data.Add("Gpu", name);
            data.Add("Cpu", name);
        }

        return data;
    }

    // P1: the significand, exponent and sign equal RZ_p of the exact rational value (BigFloatReference.RoundTowardZero).
    // P2: the result satisfies the definition of RZ by itself, R·2^e <= |v| < (R + 1)·2^e with R of exactly p bits, so a
    // mistake that the reference shares with the provider still fails.
    // mutant: GSharpKernelFloatProvider.cs:214 / CSharpCpuBigFloatArithmetic.cs:254 `(resultSig.NumWords + rightSig.NumWords) * 32`
    // -> `resultSig.NumWords * 32 * 2` (DivideNarrowByWide1, DivideNarrowByWide4 on both engines)
    // mutant: GSharpKernelFloatProvider.cs:252 `valueSig.NumWords + resultSig.NumWords + 1` -> `2 * valueSig.NumWords + MarginWords`
    // (ReciprocalOfThree8, Gpu)
    // mutant: CSharpCpuBigFloatArithmetic.cs:317 `RentTemp(quotientWords)` -> `RentTemp(resultSig.NumWords * 2)`
    // (ReciprocalOfThreeInNineWords, Cpu)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(CasesOnBothEngines))]
    public void FourOperations_AreRoundedTowardZero(string engine, string caseName)
    {
        var arithmeticCase = s_cases[caseName];
        var p = 32 * arithmeticCase.Words;
        var (num, den) = ExactValue(arithmeticCase);
        var (expectedSig, expectedExp) = BigFloatReference.RoundTowardZero(BigInteger.Abs(num), den, p);
        var expectedNegative = num.Sign < 0;

        var (sig, result) = Run(Arithmetic(engine), arithmeticCase);

        Assert.True(
            (expectedSig, expectedExp, expectedNegative) == (sig, (long)result.Exponent, result.IsNegative),
            $"{caseName} [{engine}]: expected {BigFloatReference.Describe(expectedSig, (int)expectedExp, expectedNegative)}, "
            + $"actual {BigFloatReference.Describe(sig, result.Exponent, result.IsNegative)}");
        Assert.True(
            BigFloatReference.IsRoundedTowardZero(sig, result.Exponent, BigInteger.Abs(num), den, p),
            $"{caseName} [{engine}]: {BigFloatReference.Describe(sig, result.Exponent, result.IsNegative)} is not RZ_{p}");
    }

    /// <summary>
    /// Square root inputs <c>S·2^E</c> with the number of result words: perfect squares (9 and 25, 9 scaled by an odd
    /// power of two), a non-square with an odd exponent, the all-ones word with an odd exponent, and a 3-word value.
    /// </summary>
    public static TheoryData<uint[], int, int> RootInputs() => new()
    {
        { [9u], 0, 4 },
        { [25u], 0, 1 },
        { [9u], -7, 8 },
        { [3u], 1, 4 },
        { [0xFFFF_FFFFu], 1, 4 },
        { [0x1234_5678u, 0x9ABC_DEF0u, 0x8000_0001u], 3, 4 },
    };

    // The CPU provider's Sqrt and Rsqrt already return RZ_p (they take the integer square root of an exact scaled
    // value). These cases pin that, and check the references RzSqrt and RzRsqrt by their definitions:
    // R²·2^(2e) <= a < (R + 1)²·2^(2e) and R²·2^(2e)·a <= 1 < (R + 1)²·2^(2e)·a, with R of exactly p bits.
    // mutant: CSharpCpuBigFloatArithmetic.cs:370 Sqrt `e--` -> `e++` (every odd exponent: 3, 1, 1, -7)
    // mutant: CSharpCpuBigFloatArithmetic.cs:430 `(valueSig.NumWords + resultSig.NumWords * 2) * 32` -> `resultSig.NumWords * 32`
    // (the 3-word value and 0xFFFF_FFFF: fewer than p bits)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(RootInputs))]
    public void CpuSqrtAndRsqrt_AreRoundedTowardZero(uint[] words, int exponent, int resultWords)
    {
        var p = 32 * resultWords;
        var a = new Dyadic(FromWords(words), exponent);
        using var value = new GpuBigUInt(_workspace, words);
        using var result = new GpuBigUInt(_workspace, resultWords);
        var context = CreateContext();
        var operand = new GpuFloatOperand(value.Handle, exponent);

        var sqrt = CSharpCpuBigFloatArithmetic.Instance.Sqrt(ref context, in operand, result.Handle);
        var root = result.ToBigInteger();
        Assert.Equal(BigFloatReference.RzSqrt(a.Mantissa, a.Exponent, p), (root, (long)sqrt.Exponent));
        var square = new Dyadic(root * root, 2L * sqrt.Exponent);
        var nextSquare = new Dyadic((root + 1) * (root + 1), 2L * sqrt.Exponent);
        Assert.True(root.GetBitLength() == p && square <= a && a < nextSquare, $"Sqrt: 0x{root:X} * 2^{sqrt.Exponent}");

        var rsqrt = CSharpCpuBigFloatArithmetic.Instance.Rsqrt(ref context, in operand, result.Handle);
        var inverse = result.ToBigInteger();
        Assert.Equal(BigFloatReference.RzRsqrt(a.Mantissa, a.Exponent, p), (inverse, (long)rsqrt.Exponent));
        var low = new Dyadic(inverse * inverse, 2L * rsqrt.Exponent) * a;
        var high = new Dyadic((inverse + 1) * (inverse + 1), 2L * rsqrt.Exponent) * a;
        Assert.True(inverse.GetBitLength() == p && low <= Dyadic.One && Dyadic.One < high, $"Rsqrt: 0x{inverse:X} * 2^{rsqrt.Exponent}");
    }

    private static Dictionary<string, ArithmeticCase> CreateCases()
    {
        // Seed 661 (the issue number) keeps the values stable across runs.
        var random = new Random(661);
        var wide = RandomWords(random, 9);
        var narrow = RandomWords(random, 1);
        uint[] allOnes = [uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue];
        uint[] threeInNineWords = [3u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u];

        return new Dictionary<string, ArithmeticCase>
        {
            // Add: an exact small sum; far exponents (the aligned sum has 201 bits); a borrow through every word
            // (2^100 - 2^-100 is all ones below 2^100); a sum that cancels to zero; a sum that stays below a carry;
            // operands of different widths.
            ["AddSmallIntegers"] = Binary(FloatOperation.Add, new([3u], 0), new([5u], 0), 1),
            ["AddFarExponents"] = Binary(FloatOperation.Add, new([1u], 100), new([1u], -100), 4),
            ["AddBorrowThroughEveryWord"] = Binary(FloatOperation.Add, new([1u], 100), new([1u], -100, true), 4),
            ["AddCancelsToZero"] = Binary(FloatOperation.Add, new(wide, -3), new(wide, -3, true), 4),
            ["AddAllOnesPlusHalf"] = Binary(FloatOperation.Add, new(allOnes, 0), new([1u], -1), 4),
            ["AddWideAndNarrow"] = Binary(FloatOperation.Add, new(wide, 0), new(narrow, 17), 4),

            // Multiply: exact; a 256-bit product truncated to 128 bits; a negative product of different widths;
            // exponents that cancel.
            ["MultiplySmallIntegers"] = Binary(FloatOperation.Multiply, new([3u], 0), new([5u], 0), 1),
            ["MultiplyAllOnes"] = Binary(FloatOperation.Multiply, new(allOnes, 0), new(allOnes, 0), 4),
            ["MultiplyWideByNarrow"] = Binary(FloatOperation.Multiply, new(wide, 0), new(narrow, -5, true), 8),
            ["MultiplyFarExponents"] = Binary(FloatOperation.Multiply, new([3u], -100), new([7u], 100), 1),

            // Divide: 1/3 at several widths; a 1-word dividend over a 9-word divisor (bits(R) - bits(L) > p, where a
            // shift of only 2p left fewer than p bits in the quotient, or none); the reverse; a value over itself; a
            // divisor with leading zero words; far exponents with a negative divisor.
            ["DivideOneByThree1"] = Binary(FloatOperation.Divide, new([1u], 0), new([3u], 0), 1),
            ["DivideOneByThree4"] = Binary(FloatOperation.Divide, new([1u], 0), new([3u], 0), 4),
            ["DivideOneByThree8"] = Binary(FloatOperation.Divide, new([1u], 0), new([3u], 0), 8),
            ["DivideNarrowByWide1"] = Binary(FloatOperation.Divide, new(narrow, 0), new(wide, 0), 1),
            ["DivideNarrowByWide4"] = Binary(FloatOperation.Divide, new(narrow, 0), new(wide, 0), 4),
            ["DivideWideByNarrow"] = Binary(FloatOperation.Divide, new(wide, 0), new(narrow, 0), 4),
            ["DivideSameValue"] = Binary(FloatOperation.Divide, new(wide, 7), new(wide, 7), 4),
            ["DivideByThreeInNineWords"] = Binary(FloatOperation.Divide, new([1u], 0), new(threeInNineWords, 0), 4),
            ["DivideFarExponents"] = Binary(FloatOperation.Divide, new([1u], -100), new([3u], 100, true), 4),

            // Reciprocal: 1/3 at several widths (with 2v + 2 working words, 8 result words had fewer than p bits); a
            // wide value; 3 stored in 9 words (the result must not depend on the leading zero words); a negative
            // scaled value.
            ["ReciprocalOfThree1"] = Unary(FloatOperation.Reciprocal, new([3u], 0), 1),
            ["ReciprocalOfThree4"] = Unary(FloatOperation.Reciprocal, new([3u], 0), 4),
            ["ReciprocalOfThree8"] = Unary(FloatOperation.Reciprocal, new([3u], 0), 8),
            ["ReciprocalOfWide"] = Unary(FloatOperation.Reciprocal, new(wide, 0), 4),
            ["ReciprocalOfThreeInNineWords"] = Unary(FloatOperation.Reciprocal, new(threeInNineWords, 0), 4),
            ["ReciprocalNegativeScaled"] = Unary(FloatOperation.Reciprocal, new([5u], -7, true), 4),
        };
    }

    private static ArithmeticCase Binary(FloatOperation operation, Operand left, Operand right, int words) =>
        new(operation, left, right, words);

    private static ArithmeticCase Unary(FloatOperation operation, Operand value, int words) =>
        new(operation, value, null, words);

    // The exact value as num / den (den > 0, the sign in num), from the operands the test holds.
    private static (BigInteger Num, BigInteger Den) ExactValue(ArithmeticCase arithmeticCase)
    {
        var (leftNum, leftDen) = ToRational(arithmeticCase.Left);
        if (arithmeticCase.Operation == FloatOperation.Reciprocal)
        {
            return leftNum.Sign < 0 ? (-leftDen, -leftNum) : (leftDen, leftNum);
        }

        var right = arithmeticCase.Right ?? throw new InvalidOperationException("A binary case needs a right operand.");
        var (rightNum, rightDen) = ToRational(right);
        return arithmeticCase.Operation switch
        {
            FloatOperation.Add => (leftNum * rightDen + rightNum * leftDen, leftDen * rightDen),
            FloatOperation.Multiply => (leftNum * rightNum, leftDen * rightDen),
            FloatOperation.Divide => rightNum.Sign < 0
                ? (-leftNum * rightDen, -leftDen * rightNum)
                : (leftNum * rightDen, leftDen * rightNum),
            _ => throw new InvalidOperationException($"Unexpected operation {arithmeticCase.Operation}."),
        };
    }

    // ±sig·2^exp as num / den with den = 2^max(-exp, 0).
    private static (BigInteger Num, BigInteger Den) ToRational(Operand operand)
    {
        var magnitude = operand.Exponent >= 0 ? operand.Significand << operand.Exponent : operand.Significand;
        var den = operand.Exponent >= 0 ? BigInteger.One : BigInteger.One << -operand.Exponent;
        return (operand.IsNegative ? -magnitude : magnitude, den);
    }

    private (BigInteger Significand, GpuFloatResult Result) Run(IGpuBigFloatArithmetic arithmetic, ArithmeticCase arithmeticCase)
    {
        using var left = new GpuBigUInt(_workspace, arithmeticCase.Left.Words);
        using var result = new GpuBigUInt(_workspace, arithmeticCase.Words);
        var context = CreateContext();
        var leftOperand = new GpuFloatOperand(left.Handle, arithmeticCase.Left.Exponent, arithmeticCase.Left.IsNegative);
        if (arithmeticCase.Right is not { } right)
        {
            var reciprocal = arithmetic.Reciprocal(ref context, in leftOperand, result.Handle);
            return (result.ToBigInteger(), reciprocal);
        }

        using var rightSig = new GpuBigUInt(_workspace, right.Words);
        var rightOperand = new GpuFloatOperand(rightSig.Handle, right.Exponent, right.IsNegative);
        var r = arithmeticCase.Operation switch
        {
            FloatOperation.Add => arithmetic.Add(ref context, in leftOperand, in rightOperand, result.Handle),
            FloatOperation.Multiply => arithmetic.Multiply(ref context, in leftOperand, in rightOperand, result.Handle),
            FloatOperation.Divide => arithmetic.Divide(ref context, in leftOperand, in rightOperand, result.Handle),
            _ => throw new InvalidOperationException($"Unexpected operation {arithmeticCase.Operation}."),
        };
        return (result.ToBigInteger(), r);
    }

    private static uint[] RandomWords(Random random, int count)
    {
        var words = new uint[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = (uint)random.NextInt64(0, 1L << 32);
        }

        // The top word is non-zero, so the operand has its full width.
        words[^1] |= 0x8000_0000u;
        return words;
    }

    private static BigInteger FromWords(uint[] words)
    {
        var value = BigInteger.Zero;
        for (var i = words.Length - 1; i >= 0; i--)
        {
            value = (value << 32) | words[i];
        }

        return value;
    }

    private (BigInteger Significand, GpuFloatResult Result) Normalize(
        IGpuBigFloatArithmetic arithmetic, uint[] words, int exponent, int resultWords)
    {
        using var value = new GpuBigUInt(_workspace, words);
        using var result = new GpuBigUInt(_workspace, resultWords);
        var context = CreateContext();
        var r = arithmetic.Normalize(ref context, new GpuFloatOperand(value.Handle, exponent), result.Handle);
        return (result.ToBigInteger(), r);
    }

    private GpuBigIntExecutionContext CreateContext() =>
        new(_workspace, _workspace.MemoryManager, new LegacyProviderArithmeticAdapter(), _workspace.Stream);

    private static IGpuBigFloatArithmetic Arithmetic(string engine) =>
        engine == "Gpu" ? GSharpKernelFloatProvider.Instance : CSharpCpuBigFloatArithmetic.Instance;
}
