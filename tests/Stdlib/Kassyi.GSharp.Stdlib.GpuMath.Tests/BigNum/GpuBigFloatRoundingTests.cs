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
    // mutant: GSharpKernelFloatProvider.cs:70 `checked((int)(exponent - shift))` -> `unchecked(...)` (Gpu)
    // mutant: CSharpCpuBigFloatArithmetic.cs:101 `checked((int)(exponent - shift))` -> `unchecked(...)` (Cpu)
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
    // mutant: GSharpKernelFloatProvider.cs:221 / CSharpCpuBigFloatArithmetic.cs:265 `(resultSig.NumWords + rightSig.NumWords) * 32`
    // -> `resultSig.NumWords * 32 * 2` (DivideNarrowByWide1, DivideNarrowByWide4 on both engines)
    // mutant: GSharpKernelFloatProvider.cs:259 `valueSig.NumWords + resultSig.NumWords + 1` -> `2 * valueSig.NumWords + MarginWords`
    // (ReciprocalOfThree8, Gpu)
    // mutant: CSharpCpuBigFloatArithmetic.cs:328 `RentTemp(quotientWords)` -> `RentTemp(resultSig.NumWords * 2)`
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

    // P1 and P2 over a seeded sweep: operands of 1 to 5 words, some with leading zero words, exponents in [-300, 300],
    // both signs, results of 1 to 5 words, for each of the four operations (the CPU takes more cases: it is fast).
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Gpu", 24)]
    [InlineData("Cpu", 120)]
    public void FourOperations_SeededSweep_AreRoundedTowardZero(string engine, int casesPerOperation)
    {
        // Seed 661 (the issue number) plus the engine, shown in every failure message.
        var seed = 661 * 7 + (engine == "Gpu" ? 1 : 0);
        var random = new Random(seed);
        var arithmetic = Arithmetic(engine);
        foreach (var operation in new[] { FloatOperation.Add, FloatOperation.Multiply, FloatOperation.Divide, FloatOperation.Reciprocal })
        {
            for (var i = 0; i < casesPerOperation; i++)
            {
                var left = SweepOperand(random, nonZero: operation == FloatOperation.Reciprocal);
                var right = SweepOperand(random, nonZero: operation == FloatOperation.Divide);
                var arithmeticCase = new ArithmeticCase(
                    operation, left, operation == FloatOperation.Reciprocal ? null : right, random.Next(1, 6));
                var p = 32 * arithmeticCase.Words;
                var (num, den) = ExactValue(arithmeticCase);
                var (expectedSig, expectedExp) = BigFloatReference.RoundTowardZero(BigInteger.Abs(num), den, p);

                var (sig, result) = Run(arithmetic, arithmeticCase);

                var label = $"seed {seed} {operation} #{i} [{engine}] n={arithmeticCase.Words}";
                Assert.True(
                    (expectedSig, expectedExp, num.Sign < 0) == (sig, (long)result.Exponent, result.IsNegative)
                    && BigFloatReference.IsRoundedTowardZero(sig, result.Exponent, BigInteger.Abs(num), den, p),
                    $"{label}: expected 0x{expectedSig:X} * 2^{expectedExp}, "
                    + $"actual {BigFloatReference.Describe(sig, result.Exponent, result.IsNegative)}");
            }
        }
    }

    // 1 to 5 random words; a quarter of them get zero top words, so the value is stored wider than it is.
    private static Operand SweepOperand(Random random, bool nonZero)
    {
        var count = random.Next(1, 6);
        var words = new uint[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = (uint)random.NextInt64(0, 1L << 32);
        }

        if (random.Next(4) == 0)
        {
            var zeros = random.Next(0, count);
            for (var i = count - zeros; i < count; i++)
            {
                words[i] = 0;
            }
        }

        if (nonZero && FromWords(words).IsZero)
        {
            words[0] = 1;
        }

        return new Operand(words, random.Next(-300, 301), random.Next(2) == 0);
    }

    /// <summary>
    /// Cases at the ends of the <see cref="int"/> exponent range, with one result word: results whose exponent fits
    /// although an intermediate exponent (<c>eL + eR</c>, <c>eL - eR - shift</c>, <c>-e - shift</c>) does not, and
    /// results whose exponent does not fit.
    /// </summary>
    private static readonly Dictionary<string, ArithmeticCase> s_limitCases = new()
    {
        // 2^(int.MaxValue + 1) is 2^31·2^(int.MaxValue - 30): it fits, but eL + eR does not.
        ["MultiplyIntermediateAboveMax"] = Binary(FloatOperation.Multiply, new([1u], int.MaxValue), new([1u], 1), 1),
        ["MultiplyAboveMax"] = Binary(FloatOperation.Multiply, new([0x8000_0000u], int.MaxValue), new([0x8000_0000u], int.MaxValue), 1),
        ["MultiplyBelowMin"] = Binary(FloatOperation.Multiply, new([1u], int.MinValue), new([1u], int.MinValue), 1),
        ["AddAtMax"] = Binary(FloatOperation.Add, new([1u], int.MaxValue), new([1u], int.MaxValue), 1),
        // The sum carries into a 33rd bit, so its exponent is int.MaxValue + 1.
        ["AddAboveMax"] = Binary(FloatOperation.Add, new([0x8000_0000u], int.MaxValue), new([0x8000_0000u], int.MaxValue), 1),
        ["AddBelowMin"] = Binary(FloatOperation.Add, new([1u], int.MinValue), new([1u], int.MinValue), 1),
        // 2^(int.MinValue + 40) fits, but eL - eR - shift (the dividend is shifted by 64 bits) does not.
        ["DivideIntermediateBelowMin"] = Binary(FloatOperation.Divide, new([1u], int.MinValue + 40), new([1u], 0), 1),
        ["DivideBelowMin"] = Binary(FloatOperation.Divide, new([1u], int.MinValue), new([1u], 1), 1),
        ["DivideAboveMax"] = Binary(FloatOperation.Divide, new([1u], int.MaxValue), new([1u], -40), 1),
        // 1 / (2^31·2^(int.MaxValue - 70)) = 2^31·2^(int.MinValue + 9) fits, but -e - shift does not.
        ["ReciprocalIntermediateBelowMin"] = Unary(FloatOperation.Reciprocal, new([0x8000_0000u], int.MaxValue - 70), 1),
        // -int.MinValue is not an int; the result (1/3)·2^(int.MaxValue + 1) fits.
        ["ReciprocalOfMinExponent"] = Unary(FloatOperation.Reciprocal, new([3u], int.MinValue), 1),
        ["ReciprocalBelowMin"] = Unary(FloatOperation.Reciprocal, new([3u], int.MaxValue), 1),
        ["ReciprocalTopBitBelowMin"] = Unary(FloatOperation.Reciprocal, new([0x8000_0000u], int.MaxValue), 1),
        // 1 / 2^(int.MinValue + 200) = 2^(int.MaxValue - 199) fits; the significand of 1 also needs the GPU working width
        // that is not a multiple of 32.
        ["ReciprocalOfOneNearMin"] = Unary(FloatOperation.Reciprocal, new([1u], int.MinValue + 200), 1),
        // Far beyond the range: the wrapped exponents would land inside it again.
        ["MultiplyFarAboveMax"] = Binary(FloatOperation.Multiply, new([1u], int.MaxValue), new([1u], 100), 1),
        ["DivideFarAboveMax"] = Binary(FloatOperation.Divide, new([1u], int.MaxValue), new([1u], -100), 1),
    };

    public static TheoryData<string, string> LimitCasesOnBothEngines()
    {
        var data = new TheoryData<string, string>();
        foreach (var name in s_limitCases.Keys)
        {
            data.Add("Gpu", name);
            data.Add("Cpu", name);
        }

        return data;
    }

    // The exponent of the result is computed without wrapping around: a result whose exponent fits in int is RZ_p of the
    // exact value even when an intermediate exponent does not, and a result whose exponent does not fit throws
    // OverflowException (#661 §4.1). The reference keeps the power of two apart, so the exponents never become shifts.
    // mutant: GSharpKernelFloatProvider.cs:70 / CSharpCpuBigFloatArithmetic.cs:101 `checked((int)(exponent - shift))` ->
    // `unchecked(...)` (the *AboveMax and *BelowMin cases that reach NormalizeScaled)
    // mutant: GSharpKernelFloatProvider.cs:185 / CSharpCpuBigFloatArithmetic.cs:210 `(long)left.Exponent + right.Exponent` ->
    // int (Multiply*)
    // mutant: GSharpKernelFloatProvider.cs:232 / CSharpCpuBigFloatArithmetic.cs:268 `(long)left.Exponent - right.Exponent` ->
    // int (DivideIntermediateBelowMin, DivideBelowMin)
    // mutant: GSharpKernelFloatProvider.cs:266 / CSharpCpuBigFloatArithmetic.cs:323 `-(long)value.Exponent` -> `-value.Exponent`
    // (ReciprocalBelowMin; ReciprocalIntermediateBelowMin on the GPU)
    // mutant: CSharpCpuBigFloatArithmetic.cs:179 `checked(smaller.Exponent - shift)` -> `unchecked(...)` (AddAboveMax, AddBelowMin)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(LimitCasesOnBothEngines))]
    public void FourOperations_AtTheExponentLimits_RoundOrThrow(string engine, string caseName)
    {
        var arithmeticCase = s_limitCases[caseName];
        var p = 32 * arithmeticCase.Words;
        var (num, den, scale) = ScaledExactValue(arithmeticCase);
        var (expectedSig, rzExp) = BigFloatReference.RoundTowardZero(BigInteger.Abs(num), den, p);
        var expectedExp = expectedSig.IsZero ? 0 : rzExp + scale;
        var arithmetic = Arithmetic(engine);

        if (expectedExp is < int.MinValue or > int.MaxValue)
        {
            Assert.Throws<OverflowException>(() => Run(arithmetic, arithmeticCase));
            return;
        }

        var (sig, result) = Run(arithmetic, arithmeticCase);
        Assert.True(
            (expectedSig, expectedExp, num.Sign < 0) == (sig, (long)result.Exponent, result.IsNegative),
            $"{caseName} [{engine}]: expected 0x{expectedSig:X} * 2^{expectedExp}, "
            + $"actual {BigFloatReference.Describe(sig, result.Exponent, result.IsNegative)}");
    }

    // The exact value as num / den · 2^scale (den > 0, the sign in num), with the powers of two of the exponents kept in
    // scale.
    private static (BigInteger Num, BigInteger Den, long Scale) ScaledExactValue(ArithmeticCase arithmeticCase)
    {
        var left = arithmeticCase.Left;
        var leftSig = left.IsNegative ? -left.Significand : left.Significand;
        if (arithmeticCase.Operation == FloatOperation.Reciprocal)
        {
            return (left.IsNegative ? BigInteger.MinusOne : BigInteger.One, left.Significand, -(long)left.Exponent);
        }

        var right = arithmeticCase.Right ?? throw new InvalidOperationException("A binary case needs a right operand.");
        var rightSig = right.IsNegative ? -right.Significand : right.Significand;
        switch (arithmeticCase.Operation)
        {
            case FloatOperation.Add:
                long low = Math.Min(left.Exponent, right.Exponent);
                return ((leftSig << (int)(left.Exponent - low)) + (rightSig << (int)(right.Exponent - low)), BigInteger.One, low);
            case FloatOperation.Multiply:
                return (leftSig * rightSig, BigInteger.One, (long)left.Exponent + right.Exponent);
            case FloatOperation.Divide:
                return (rightSig.Sign < 0 ? -leftSig : leftSig, right.Significand, (long)left.Exponent - right.Exponent);
            default:
                throw new InvalidOperationException($"Unexpected operation {arithmeticCase.Operation}.");
        }
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
    // mutant: CSharpCpuBigFloatArithmetic.cs:381 Sqrt `e--` -> `e++` (every odd exponent: 3, 1, 1, -7)
    // mutant: CSharpCpuBigFloatArithmetic.cs:441 `(valueSig.NumWords + resultSig.NumWords * 2) * 32` -> `resultSig.NumWords * 32`
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

    /// <summary>
    /// A square root input <c>S·2^E</c> stored in exactly <c>Words.Length</c> words, with the number of result words.
    /// </summary>
    private sealed record RootCase(uint[] Words, int Exponent, int ResultWords);

    private static readonly Dictionary<string, RootCase> s_sqrtCases = CreateSqrtCases();

    public static TheoryData<string, string> SqrtCasesOnBothEngines()
    {
        var data = new TheoryData<string, string>();
        foreach (var name in s_sqrtCases.Keys)
        {
            data.Add("Gpu", name);
            data.Add("Cpu", name);
        }

        return data;
    }

    // P1: Sqrt returns RZ_p(sqrt(a)), the reference RzSqrt (an integer square root of the test's own, checked against
    // r² <= A < (r + 1)²; not the product's). P2: the result satisfies the definition by itself,
    // R²·2^(2e) <= a < (R + 1)²·2^(2e) with R of exactly p bits, so an exact root (PerfectSquares) is returned exactly.
    // mutant: GSharpKernelFloatProvider.cs:302 `% 2 != 0` -> `% 2 == 0` (every case: the root of 2·a, or an odd shift)
    // mutant: GSharpKernelFloatProvider.cs:308 `checked((int)shift)` -> `checked((int)Math.Max(shift, 0))` (Width9, Width16, Width16ZeroLow)
    // mutant: GSharpKernelFloatProvider.cs:301 `2L * 32 * n` -> `2 * 32 * n` (ParityMin, ParityMinPlus1: E - t wraps)
    // mutant: GpuBigIntDivRem.Sqrt.cs:117 `+ 48` -> `+ 47` (the estimate is twice the root)
    // mutant: GpuBigIntSqrtKernels.cu.cs:176 `AddSmallSigned(trial, slackWords, -1)` removed (NearSquarePlus2R steps to r + 1,
    // NearSquareMinus1)
    // mutant: GpuBigIntSqrtKernels.cu.cs:192 `AddSmallSigned(root, rootWords, 1)` -> `0` (NearSquareExact, NearSquarePlus1,
    // NearSquareNext, SquareWide: the estimate one below the root is not raised)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SqrtCasesOnBothEngines))]
    public void Sqrt_IsRoundedTowardZero(string engine, string caseName)
    {
        var rootCase = s_sqrtCases[caseName];
        var p = 32 * rootCase.ResultWords;
        var a = new Dyadic(FromWords(rootCase.Words), rootCase.Exponent);
        var (expectedSig, expectedExp) = BigFloatReference.RzSqrt(a.Mantissa, a.Exponent, p);

        using var value = new GpuBigUInt(_workspace, rootCase.Words);
        using var result = new GpuBigUInt(_workspace, rootCase.ResultWords);
        var context = CreateContext();
        var operand = new GpuFloatOperand(value.Handle, rootCase.Exponent);
        var sqrt = Arithmetic(engine).Sqrt(ref context, in operand, result.Handle);
        var root = result.ToBigInteger();

        Assert.True(
            (expectedSig, expectedExp, false) == (root, (long)sqrt.Exponent, sqrt.IsNegative),
            $"{caseName} [{engine}]: expected 0x{expectedSig:X} * 2^{expectedExp}, actual 0x{root:X} * 2^{sqrt.Exponent}");
        var square = new Dyadic(root * root, 2L * sqrt.Exponent);
        var nextSquare = new Dyadic((root + 1) * (root + 1), 2L * sqrt.Exponent);
        Assert.True(
            root.GetBitLength() == p && square <= a && a < nextSquare,
            $"{caseName} [{engine}]: 0x{root:X} * 2^{sqrt.Exponent} is not RZ_{p}(sqrt(a))");
    }

    // The square root of a negative value is not defined; the square root of zero is positive zero.
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Gpu")]
    [InlineData("Cpu")]
    public void Sqrt_NegativeThrows_ZeroIsZero(string engine)
    {
        var arithmetic = Arithmetic(engine);
        using var value = new GpuBigUInt(_workspace, [9u]);
        using var zero = new GpuBigUInt(_workspace, [0u, 0u]);
        using var result = new GpuBigUInt(_workspace, [0x1234_5678u, 1u, 2u, 3u]);
        var context = CreateContext();

        var negative = new GpuFloatOperand(value.Handle, 0, true);
        Assert.Throws<InvalidOperationException>(() =>
        {
            var c = CreateContext();
            arithmetic.Sqrt(ref c, in negative, result.Handle);
        });

        var zeroOperand = new GpuFloatOperand(zero.Handle, 5);
        var sqrt = arithmetic.Sqrt(ref context, in zeroOperand, result.Handle);
        Assert.Equal((BigInteger.Zero, 0, false), (result.ToBigInteger(), sqrt.Exponent, sqrt.IsNegative));
    }

    private static Dictionary<string, RootCase> CreateSqrtCases()
    {
        var cases = new Dictionary<string, RootCase>
        {
            // PerfectSquares: an exact root is returned exactly. Powers of two have an exact inverse square root
            // (theta = 0 in the design); 9, 25 and the wide square do not, so the approximation lands below the root
            // and the correction has to step up.
            ["Square4n1"] = new([4u], 0, 1),
            ["Square9n1"] = new([9u], 0, 1),
            ["Square9n4"] = new([9u], 0, 4),
            ["Square9n8"] = new([9u], 0, 8),
            ["Square25n4"] = new([25u], 0, 4),
            ["Square9Scaled"] = new([9u], 74, 4),
            ["SquareWide"] = new(ToWords(BigInteger.Pow((BigInteger.One << 64) + (BigInteger.One << 33) + 1, 2)), 0, 4),

            // ExponentParity: the same significand at even, odd, large and extreme exponents (E - t computed in long).
            ["Parity0"] = new([0x9ABC_DEF0u, 0x1234_5678u], 0, 4),
            ["Parity1"] = new([0x9ABC_DEF0u, 0x1234_5678u], 1, 4),
            ["ParityMinus1"] = new([0x9ABC_DEF0u, 0x1234_5678u], -1, 4),
            ["ParityLarge"] = new([0x9ABC_DEF0u, 0x1234_5678u], 1 << 30, 4),
            ["ParityLargeNegative"] = new([0x9ABC_DEF0u, 0x1234_5678u], -(1 << 30), 4),
            ["ParityMax"] = new([0x9ABC_DEF0u, 0x1234_5678u], int.MaxValue, 4),
            ["ParityMin"] = new([0x9ABC_DEF0u, 0x1234_5678u], int.MinValue, 4),
            ["ParityMinPlus1"] = new([0x9ABC_DEF0u, 0x1234_5678u], int.MinValue + 1, 4),
        };

        // NearSquares (n = 4): A = r² + d for a 128-bit r is used as it is (t = 0), so the root is r - 1, r or r + 1
        // and the remainder spans its whole range [0, 2r]: r² - 1, r², r² + 1, r² + 2r (the largest remainder) and
        // (r + 1)².
        var r = (BigInteger.One << 127) + BigInteger.Parse("0123456789ABCDEF0123456789ABCDE", System.Globalization.NumberStyles.HexNumber);
        cases["NearSquareMinus1"] = new(ToWords(r * r - 1, 8), 0, 4);
        cases["NearSquareExact"] = new(ToWords(r * r, 8), 0, 4);
        cases["NearSquarePlus1"] = new(ToWords(r * r + 1, 8), 0, 4);
        cases["NearSquarePlus2R"] = new(ToWords(r * r + 2 * r, 8), 0, 4);
        cases["NearSquareNext"] = new(ToWords((r + 1) * (r + 1), 8), 0, 4);

        // PrecisionLadder: every result width that changes the margin of the approximation (m = ceil(n/2)) and the
        // widths of the old fixed 4-word margin, for 3, 7 and a random n-word value (seed 42 + 37n).
        foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 8, 9, 16, 17, 32, 33 })
        {
            cases[$"Ladder3n{n}"] = new([3u], 0, n);
            cases[$"Ladder7n{n}"] = new([7u], 1, n);
            cases[$"LadderRandomn{n}"] = new(RandomWords(new Random(42 + 37 * n), n), -5, n);
        }

        // InputWidth (n = 4): operands of 1, n, 2n, 2n + 1 and 4n words, so A is shifted left (t > 0), taken as it
        // is (t = 0) or truncated (t < 0, with set and with zero dropped bits).
        var widthRandom = new Random(661);
        cases["Width1"] = new(RandomWords(widthRandom, 1), 0, 4);
        cases["Width4"] = new(RandomWords(widthRandom, 4), 0, 4);
        cases["Width8"] = new(RandomWords(widthRandom, 8), 0, 4);
        cases["Width9"] = new(RandomWords(widthRandom, 9), 1, 4);
        cases["Width16"] = new(RandomWords(widthRandom, 16), 0, 4);
        var high = RandomWords(widthRandom, 8);
        cases["Width16ZeroLow"] = new([0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, .. high], 0, 4);
        return cases;
    }

    private static readonly Dictionary<string, RootCase> s_rsqrtCases = CreateRsqrtCases();

    public static TheoryData<string, string> RsqrtCasesOnBothEngines()
    {
        var data = new TheoryData<string, string>();
        foreach (var name in s_rsqrtCases.Keys)
        {
            data.Add("Gpu", name);
            data.Add("Cpu", name);
        }

        return data;
    }

    // P1: Rsqrt returns RZ_p(1 / sqrt(a)), the reference RzRsqrt (the test's own integer square root of
    // floor(2^(2k) / s)). P2: the result satisfies the definition by itself, R²·2^(2e)·a <= 1 < (R + 1)²·2^(2e)·a with R of
    // exactly p bits, so an exact inverse root (a power of four) is returned exactly.
    // mutant: GSharpKernelFloatProvider.cs:360 `n - words / 2` -> `n - words` (Ladder3n4, Width1, Width4, Parity*: fewer than
    // p bits)
    // mutant: GSharpKernelFloatProvider.cs:347 `if (shift > 0)` -> `if (true)` (OddExponentFullWidth, OddExponentFullWidthWide:
    // the radicand loses its lowest bit and the result is 1 ulp too large)
    // mutant: GSharpKernelFloatProvider.cs:345 `% 2 != 0` -> `% 2 == 0` (every case with an odd shift)
    // mutant: GSharpKernelFloatProvider.cs:367 `- 32L * (words + marginWords)` -> `... + 1` (every case: twice the value)
    // mutant: GSharpKernelFloatProvider.cs:344 `long shift = 32L * ...` -> `int shift = 32 * ...` (ParityMin, ParityMinPlus1)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(RsqrtCasesOnBothEngines))]
    public void Rsqrt_IsRoundedTowardZero(string engine, string caseName)
    {
        var rootCase = s_rsqrtCases[caseName];
        var p = 32 * rootCase.ResultWords;
        var a = new Dyadic(FromWords(rootCase.Words), rootCase.Exponent);
        var (expectedSig, expectedExp) = BigFloatReference.RzRsqrt(a.Mantissa, a.Exponent, p);

        using var value = new GpuBigUInt(_workspace, rootCase.Words);
        using var result = new GpuBigUInt(_workspace, rootCase.ResultWords);
        var context = CreateContext();
        var operand = new GpuFloatOperand(value.Handle, rootCase.Exponent);
        var rsqrt = Arithmetic(engine).Rsqrt(ref context, in operand, result.Handle);
        var inverse = result.ToBigInteger();

        Assert.True(
            (expectedSig, expectedExp, false) == (inverse, (long)rsqrt.Exponent, rsqrt.IsNegative),
            $"{caseName} [{engine}]: expected 0x{expectedSig:X} * 2^{expectedExp}, actual 0x{inverse:X} * 2^{rsqrt.Exponent}");
        var low = new Dyadic(inverse * inverse, 2L * rsqrt.Exponent) * a;
        var high = new Dyadic((inverse + 1) * (inverse + 1), 2L * rsqrt.Exponent) * a;
        Assert.True(
            inverse.GetBitLength() == p && low <= Dyadic.One && Dyadic.One < high,
            $"{caseName} [{engine}]: 0x{inverse:X} * 2^{rsqrt.Exponent} is not RZ_{p}(1 / sqrt(a))");
    }

    // The inverse square root of a negative value is not defined, and that of zero is a division by zero.
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Gpu")]
    [InlineData("Cpu")]
    public void Rsqrt_NegativeAndZeroThrow(string engine)
    {
        var arithmetic = Arithmetic(engine);
        using var value = new GpuBigUInt(_workspace, [9u]);
        using var zero = new GpuBigUInt(_workspace, [0u, 0u]);
        using var result = new GpuBigUInt(_workspace, 4);
        var negative = new GpuFloatOperand(value.Handle, 0, true);
        var zeroOperand = new GpuFloatOperand(zero.Handle, 5);

        Assert.Throws<InvalidOperationException>(() =>
        {
            var c = CreateContext();
            arithmetic.Rsqrt(ref c, in negative, result.Handle);
        });
        Assert.Throws<DivideByZeroException>(() =>
        {
            var c = CreateContext();
            arithmetic.Rsqrt(ref c, in zeroOperand, result.Handle);
        });
    }

    private static Dictionary<string, RootCase> CreateRsqrtCases()
    {
        var cases = new Dictionary<string, RootCase>
        {
            // Exact inverse roots (powers of four, either exponent parity) and an irrational one.
            ["PowerOfFourEven"] = new([4u], 0, 4),
            ["PowerOfFourOdd"] = new([2u], 1, 4),
            ["PowerOfFourWide"] = new([0u, 0u, 1u], -64, 4),
            // One above a power of four: the inverse root lies just below an exact value (2^-32 for 2^64 + 1), where a
            // result rounded up would land on the exact value.
            ["PowerOfFourPlus1"] = new([5u], 0, 4),
            ["PowerOfFourPlus1Wide"] = new([1u, 0u, 1u], 0, 4),
            ["Two"] = new([2u], 0, 4),
            ["Nine"] = new([9u], 0, 8),

            // The significand already fills its words (no leading zero bit) and the exponent is odd: the value has to
            // move one word up instead of losing its lowest bit (losing it would round the inverse root up).
            ["OddExponentFullWidth"] = new([0xFFFF_FFFFu], 1, 4),
            ["OddExponentFullWidthWide"] = new([0x1234_5679u, 0x9ABC_DEF0u, 0x0FED_CBA9u, 0x8765_4321u], -3, 4),

            // ExponentParity: the same significand at even, odd, large and extreme exponents (computed in long).
            ["Parity0"] = new([0x9ABC_DEF0u, 0x1234_5678u], 0, 4),
            ["Parity1"] = new([0x9ABC_DEF0u, 0x1234_5678u], 1, 4),
            ["ParityMinus1"] = new([0x9ABC_DEF0u, 0x1234_5678u], -1, 4),
            ["ParityMax"] = new([0x9ABC_DEF0u, 0x1234_5678u], int.MaxValue, 4),
            ["ParityMin"] = new([0x9ABC_DEF0u, 0x1234_5678u], int.MinValue, 4),
            ["ParityMinPlus1"] = new([0x9ABC_DEF0u, 0x1234_5678u], int.MinValue + 1, 4),
        };

        // PrecisionLadder: every result width that changes the margin m = max(1, n - floor(w/2)), and the widths of the
        // old fixed 4-word margin (at 5 words and more only about 16n + 65 bits were right).
        foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 8, 9, 16, 17, 32, 33 })
        {
            cases[$"Ladder3n{n}"] = new([3u], 0, n);
            cases[$"Ladder7n{n}"] = new([7u], 1, n);
            cases[$"LadderRandomn{n}"] = new(RandomWords(new Random(42 + 37 * n), n), -5, n);
        }

        // InputWidth (n = 4): operands of 1, n, 2n, 2n + 1 and 4n words; the radicand is never truncated.
        var widthRandom = new Random(662);
        cases["Width1"] = new(RandomWords(widthRandom, 1), 0, 4);
        cases["Width4"] = new(RandomWords(widthRandom, 4), 0, 4);
        cases["Width8"] = new(RandomWords(widthRandom, 8), 1, 4);
        cases["Width9"] = new(RandomWords(widthRandom, 9), 0, 4);
        cases["Width16"] = new(RandomWords(widthRandom, 16), 1, 4);
        cases["Width17"] = new(RandomWords(widthRandom, 17), 0, 4);
        return cases;
    }

    private static uint[] ToWords(BigInteger value) =>
        ToWords(value, Math.Max(1, (int)((value.GetBitLength() + 31) / 32)));

    private static uint[] ToWords(BigInteger value, int count)
    {
        var words = new uint[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = (uint)((value >> (32 * i)) & uint.MaxValue);
        }

        return words;
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

            // A significand of 1: floor(2^T / 1) = 2^T needs T + 1 bits, so a working width of exactly T bits failed on
            // the GPU (the device correction did not converge). 1 at several widths, and 1 stored in 9 words.
            // mutant: GSharpKernelFloatProvider.cs:260 `reciprocalWords * 32 - 1` -> `reciprocalWords * 32 - 0` (all four on
            // the GPU, and ReciprocalOfOneNearMin in the limit cases)
            ["ReciprocalOfOne1"] = Unary(FloatOperation.Reciprocal, new([1u], 3), 1),
            ["ReciprocalOfOne4"] = Unary(FloatOperation.Reciprocal, new([1u], 0), 4),
            ["ReciprocalOfOne9"] = Unary(FloatOperation.Reciprocal, new([1u], 0), 9),
            ["ReciprocalOfOneInNineWords"] = Unary(FloatOperation.Reciprocal, new([1u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u], 0), 4),
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
