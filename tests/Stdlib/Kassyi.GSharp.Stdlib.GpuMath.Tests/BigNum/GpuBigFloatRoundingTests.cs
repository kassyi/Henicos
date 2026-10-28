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

    /// <summary>
    /// The provider a case runs on: the G# kernels (<see cref="GSharpKernelFloatProvider"/>) or the C# reference
    /// (<see cref="CSharpCpuBigFloatArithmetic"/>).
    /// </summary>
    public enum Engine
    {
        Gpu,
        Cpu,
    }

    // Normalize moves the top bit of the significand to the top of the result and changes the exponent by the same
    // shift; an exponent outside int throws OverflowException instead of wrapping around (#661 §4.10). The last value
    // on each side that fits is returned exactly.
    // mutant: GSharpKernelFloatProvider.cs:70 `checked((int)(exponent - shift))` -> `unchecked(...)` (Gpu)
    // mutant: CSharpCpuBigFloatArithmetic.cs:101 `checked((int)(exponent - shift))` -> `unchecked(...)` (Cpu)
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(Engine.Gpu)]
    [InlineData(Engine.Cpu)]
    public void Normalize_ExponentOutOfRange_ThrowsOverflow(Engine engine)
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

    // The operator * follows the contract of IGpuBigFloatArithmetic.Multiply, like / follows Divide: the exponents are
    // added in long and the product is RZ_p for the width of the wider operand. It used to multiply the significands as
    // integers and add the exponents in int, which wrapped around: 2^int.MinValue · 2^int.MinValue came out as 1.
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(Engine.Gpu)]
    [InlineData(Engine.Cpu)]
    public void MultiplyOperator_ExponentBelowMin_ThrowsOverflow(Engine engine)
    {
        // 2^int.MinValue · 2^int.MinValue = 2^(2·int.MinValue): the exponent of the product does not fit in int.
        using var tiny = new GpuBigFloat(new GpuBigUInt(_workspace, [1u]), int.MinValue, false);
        Assert.Throws<OverflowException>(() =>
        {
            using var product = MultiplyByOperator(engine, tiny, tiny);
        });
    }

    // 2^int.MaxValue · 2^1 = 2^(int.MaxValue + 1) = 0x80000000·2^(int.MaxValue - 30): eL + eR does not fit in int, the
    // product does (it used to come out as 2^int.MinValue).
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(Engine.Gpu)]
    [InlineData(Engine.Cpu)]
    public void MultiplyOperator_IntermediateAboveMax_Fits(Engine engine)
    {
        using var huge = new GpuBigFloat(new GpuBigUInt(_workspace, [1u]), int.MaxValue, false);
        using var two = new GpuBigFloat(new GpuBigUInt(_workspace, [1u]), 1, false);
        using var product = MultiplyByOperator(engine, huge, two);
        Assert.Equal(
            (new BigInteger(0x8000_0000u), int.MaxValue - 30, false),
            (product.UFloat.Significand.ToBigInteger(), product.Exponent, product.IsNegative));
    }

    // A 4-word by 4-word product is rounded toward zero to 4 words: RZ_128 of the exact product, and the same bits as
    // Multiply with a 4-word result (it used to keep all 8 words).
    // mutant: GpuBigFloatExpressionVisitor.cs:71 VisitMul `extraWords: 0` -> `extraWords: 4` (both engines: a wider product
    // is kept; also MultiplyOperator_IntermediateAboveMax_Fits)
    // mutant: GpuBigFloatExpressionVisitor.cs:71 VisitMul `negateRight: false` -> `negateRight: true` (both engines: the
    // sign; also MultiplyOperator_IntermediateAboveMax_Fits)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(Engine.Gpu)]
    [InlineData(Engine.Cpu)]
    public void MultiplyOperator_RoundsLikeMultiply(Engine engine)
    {
        uint[] leftWords = [0x89AB_CDEFu, 0x0123_4567u, 0xFEDC_BA98u, 0x7654_3210u];
        uint[] rightWords = [0x1357_9BDFu, 0x2468_ACE0u, 0x0F1E_2D3Cu, 0x8000_0001u];
        using var left = new GpuBigFloat(new GpuBigUInt(_workspace, leftWords), -50, true);
        using var right = new GpuBigFloat(new GpuBigUInt(_workspace, rightWords), 17, false);
        using var product = MultiplyByOperator(engine, left, right);
        var actual = (product.UFloat.Significand.ToBigInteger(), (long)product.Exponent, product.IsNegative);

        // |L·R|·2^(-50 + 17), negative.
        var (expectedSig, expectedExp) = BigFloatReference.RoundTowardZero(
            FromWords(leftWords) * FromWords(rightWords), BigInteger.One << 33, 128);
        Assert.Equal((expectedSig, expectedExp, true), actual);
        var multiplyCase = Mul(Of(leftWords, -50, isNegative: true), Of(rightWords, 17), 4);
        var (sig, result) = Run(Arithmetic(engine), multiplyCase);
        Assert.Equal((sig, (long)result.Exponent, result.IsNegative), actual);
    }

    // a * b through the operator: the default visitor of the workspace (the G# kernels) for Engine.Gpu, a visitor with the
    // CPU arithmetic for Engine.Cpu.
    private GpuBigFloat MultiplyByOperator(Engine engine, GpuBigFloat left, GpuBigFloat right)
    {
        if (engine == Engine.Gpu)
        {
            GpuBigFloat product = left * right;
            return product;
        }

        var visitor = new GpuBigFloatExpressionVisitor(_workspace, CSharpCpuBigFloatArithmetic.Instance);
        return visitor.Visit(left * right);
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

    public static TheoryData<Engine, string> CasesOnBothEngines()
    {
        var data = new TheoryData<Engine, string>();
        foreach (var name in s_cases.Keys)
        {
            data.Add(Engine.Gpu, name);
            data.Add(Engine.Cpu, name);
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
    public void FourOperations_AreRoundedTowardZero(Engine engine, string caseName)
    {
        var arithmeticCase = s_cases[caseName];
        var p = 32 * arithmeticCase.Words;
        var (num, den, scale) = ScaledExactValue(arithmeticCase);
        var (expectedSig, rzExp) = BigFloatReference.RoundTowardZero(BigInteger.Abs(num), den, p);
        var expectedExp = expectedSig.IsZero ? 0 : rzExp + scale;
        var expectedNegative = num.Sign < 0;

        var (sig, result) = Run(Arithmetic(engine), arithmeticCase);

        Assert.True(
            (expectedSig, expectedExp, expectedNegative) == (sig, (long)result.Exponent, result.IsNegative),
            $"{caseName} [{engine}]: expected {BigFloatReference.Describe(expectedSig, (int)expectedExp, expectedNegative)}, "
            + $"actual {BigFloatReference.Describe(sig, result.Exponent, result.IsNegative)}");
        Assert.True(
            BigFloatReference.IsRoundedTowardZero(sig, result.Exponent - scale, BigInteger.Abs(num), den, p),
            $"{caseName} [{engine}]: {BigFloatReference.Describe(sig, result.Exponent, result.IsNegative)} is not RZ_{p}");
    }

    // P1 and P2 over a seeded sweep: operands of 1 to 5 words, some with leading zero words, exponents in [-300, 300],
    // both signs, results of 1 to 5 words, for each of the four operations (the CPU takes more cases: it is fast).
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(Engine.Gpu, 24)]
    [InlineData(Engine.Cpu, 120)]
    public void FourOperations_SeededSweep_AreRoundedTowardZero(Engine engine, int casesPerOperation)
    {
        // Seed 661 (the issue number) plus the engine, shown in every failure message.
        var seed = 661 * 7 + (engine == Engine.Gpu ? 1 : 0);
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
                var (num, den, scale) = ScaledExactValue(arithmeticCase);
                var (expectedSig, rzExp) = BigFloatReference.RoundTowardZero(BigInteger.Abs(num), den, p);
                var expectedExp = expectedSig.IsZero ? 0 : rzExp + scale;

                var (sig, result) = Run(arithmetic, arithmeticCase);

                var label = $"seed {seed} {operation} #{i} [{engine}] n={arithmeticCase.Words}";
                Assert.True(
                    (expectedSig, expectedExp, num.Sign < 0) == (sig, (long)result.Exponent, result.IsNegative)
                    && BigFloatReference.IsRoundedTowardZero(sig, result.Exponent - scale, BigInteger.Abs(num), den, p),
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
        // 2^int.MaxValue · 2 = 2^31·2^(int.MaxValue - 30): the product fits, but eL + eR does not.
        ["MultiplyIntermediateAboveMax"] = Mul(Pow2(int.MaxValue), Pow2(1)),
        // (2^31·2^int.MaxValue)²: the exponent of the product is above int.MaxValue.
        ["MultiplyAboveMax"] = Mul(Of(0x8000_0000u, int.MaxValue), Of(0x8000_0000u, int.MaxValue)),
        // 2^int.MinValue · 2^int.MinValue: the exponent of the product is below int.MinValue.
        ["MultiplyBelowMin"] = Mul(Pow2(int.MinValue), Pow2(int.MinValue)),
        // 2^int.MaxValue + 2^int.MaxValue = 2^31·2^(int.MaxValue - 30): the sum fits.
        ["AddAtMax"] = Add(Pow2(int.MaxValue), Pow2(int.MaxValue)),
        // (2^31 + 2^31)·2^int.MaxValue: the sum carries into a 33rd bit, so its exponent is int.MaxValue + 1.
        ["AddAboveMax"] = Add(Of(0x8000_0000u, int.MaxValue), Of(0x8000_0000u, int.MaxValue)),
        // 2^int.MinValue + 2^int.MinValue: the exponent of the normalized sum is below int.MinValue.
        ["AddBelowMin"] = Add(Pow2(int.MinValue), Pow2(int.MinValue)),
        // 2^(int.MinValue + 40) / 1: the quotient fits, but eL - eR - shift (the dividend shifted by 64 bits) does not.
        ["DivideIntermediateBelowMin"] = Div(Pow2(int.MinValue + 40), Pow2(0)),
        // 2^int.MinValue / 2: the exponent of the quotient is below int.MinValue.
        ["DivideBelowMin"] = Div(Pow2(int.MinValue), Pow2(1)),
        // 2^int.MaxValue / 2^-40: the exponent of the quotient is above int.MaxValue.
        ["DivideAboveMax"] = Div(Pow2(int.MaxValue), Pow2(-40)),
        // 1 / (2^31·2^(int.MaxValue - 70)) = 2^31·2^(int.MinValue + 9): the result fits, but -e - shift does not.
        ["ReciprocalIntermediateBelowMin"] = Rcp(Of(0x8000_0000u, int.MaxValue - 70)),
        // 1 / (3·2^int.MinValue): -int.MinValue is not an int; the result (1/3)·2^(int.MaxValue + 1) fits.
        ["ReciprocalOfMinExponent"] = Rcp(Of(3u, int.MinValue)),
        // 1 / (3·2^int.MaxValue): the exponent of the result is below int.MinValue.
        ["ReciprocalBelowMin"] = Rcp(Of(3u, int.MaxValue)),
        // 1 / (2^31·2^int.MaxValue): the same from a significand with only its top bit set.
        ["ReciprocalTopBitBelowMin"] = Rcp(Of(0x8000_0000u, int.MaxValue)),
        // 1 / 2^(int.MinValue + 200) = 2^(int.MaxValue - 199): fits; the significand 1 needs the GPU working width that
        // is not a multiple of 32.
        ["ReciprocalOfOneNearMin"] = Rcp(Pow2(int.MinValue + 200)),
        // 2^int.MaxValue · 2^100: far beyond the range, where the wrapped exponent would land inside it again.
        ["MultiplyFarAboveMax"] = Mul(Pow2(int.MaxValue), Pow2(100)),
        // 2^int.MaxValue / 2^-100: the same for a quotient.
        ["DivideFarAboveMax"] = Div(Pow2(int.MaxValue), Pow2(-100)),
    };

    public static TheoryData<Engine, string> LimitCasesOnBothEngines()
    {
        var data = new TheoryData<Engine, string>();
        foreach (var name in s_limitCases.Keys)
        {
            data.Add(Engine.Gpu, name);
            data.Add(Engine.Cpu, name);
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
    public void FourOperations_AtTheExponentLimits_RoundOrThrow(Engine engine, string caseName)
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

    // The exact value as num / den · 2^scale (den > 0, the sign in num), from the operands the test holds. The powers of
    // two of the exponents stay in scale, so an exponent at the end of the int range never becomes a shift.
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
    /// A square root input <c>S·2^E</c> (<see cref="Operand"/>; its sign is not used), with the number of result words.
    /// </summary>
    private sealed record RootCase(Operand Value, int ResultWords);

    private static readonly Dictionary<string, RootCase> s_sqrtCases = CreateSqrtCases();

    public static TheoryData<Engine, string> SqrtCasesOnBothEngines()
    {
        var data = new TheoryData<Engine, string>();
        foreach (var name in s_sqrtCases.Keys)
        {
            data.Add(Engine.Gpu, name);
            data.Add(Engine.Cpu, name);
        }

        return data;
    }

    // P1: Sqrt returns RZ_p(sqrt(a)), the reference RzSqrt (an integer square root of the test's own, checked against
    // r² <= A < (r + 1)²; not the product's). P2: the result satisfies the definition by itself,
    // R²·2^(2e) <= a < (R + 1)²·2^(2e) with R of exactly p bits, so an exact root (PerfectSquares) is returned exactly.
    // mutant: GSharpKernelFloatProvider.cs:302 `% 2 != 0` -> `% 2 == 0` (every case: the root of 2·a, or an odd shift)
    // mutant: GSharpKernelFloatProvider.cs:308 `checked((int)shift)` -> `checked((int)Math.Max(shift, 0))` (Width9, Width16, Width16ZeroLow)
    // mutant: GSharpKernelFloatProvider.cs:301 `2L * 32 * n` -> `2 * 32 * n` (ParityMin, ParityMinPlus1: E - t wraps)
    // mutant: GpuBigIntDivRem.Sqrt.cs:124 `+ 48` -> `+ 47` (the estimate is twice the root)
    // mutant: GpuBigIntSqrtKernels.cu.cs:176 `AddSmallSigned(trial, slackWords, -1)` removed (NearSquarePlus2R steps to r + 1,
    // NearSquareMinus1)
    // mutant: GpuBigIntSqrtKernels.cu.cs:192 `AddSmallSigned(root, rootWords, 1)` -> `0` (NearSquareExact, NearSquarePlus1,
    // NearSquareNext, SquareWide: the estimate one below the root is not raised)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SqrtCasesOnBothEngines))]
    public void Sqrt_IsRoundedTowardZero(Engine engine, string caseName)
    {
        var rootCase = s_sqrtCases[caseName];
        var p = 32 * rootCase.ResultWords;
        var a = new Dyadic(rootCase.Value.Significand, rootCase.Value.Exponent);
        var (expectedSig, expectedExp) = BigFloatReference.RzSqrt(a.Mantissa, a.Exponent, p);

        using var value = new GpuBigUInt(_workspace, rootCase.Value.Words);
        using var result = new GpuBigUInt(_workspace, rootCase.ResultWords);
        var context = CreateContext();
        var operand = new GpuFloatOperand(value.Handle, rootCase.Value.Exponent);
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
    [InlineData(Engine.Gpu)]
    [InlineData(Engine.Cpu)]
    public void Sqrt_NegativeThrows_ZeroIsZero(Engine engine)
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
        // 0x123456789ABCDEF0: the significand of the ExponentParity cases.
        uint[] parity = [0x9ABC_DEF0u, 0x1234_5678u];
        // 2^64 + 2^33 + 1: the root of the SquareWide case.
        var wideRoot = (BigInteger.One << 64) + (BigInteger.One << 33) + 1;
        // r = 2^127 + 0x0123456789ABCDEF0123456789ABCDE: the 128-bit root of the NearSquare cases.
        var r = (BigInteger.One << 127)
            + BigInteger.Parse("0123456789ABCDEF0123456789ABCDE", System.Globalization.NumberStyles.HexNumber);

        var cases = new Dictionary<string, RootCase>
        {
            // 4 in one word: an exact root of a power of two, whose inverse square root is exact (theta = 0).
            ["Square4n1"] = Root(Of(4u, 0), 1),
            // 9 in one word: an exact root; the approximation lands below it and the correction steps up.
            ["Square9n1"] = Root(Of(9u, 0), 1),
            // 9 in 4 words.
            ["Square9n4"] = Root(Of(9u, 0), 4),
            // 9 in 8 words.
            ["Square9n8"] = Root(Of(9u, 0), 8),
            // 25 in 4 words.
            ["Square25n4"] = Root(Of(25u, 0), 4),
            // 9·2^74: an exact root with an even exponent.
            ["Square9Scaled"] = Root(Of(9u, 74), 4),
            // (2^64 + 2^33 + 1)²: a wide exact square.
            ["SquareWide"] = Root(Of(ToWords(BigInteger.Pow(wideRoot, 2)), 0), 4),

            // ExponentParity (E - t is computed in long): an even exponent.
            ["Parity0"] = Root(Of(parity, 0), 4),
            // An odd exponent.
            ["Parity1"] = Root(Of(parity, 1), 4),
            // A negative odd exponent.
            ["ParityMinus1"] = Root(Of(parity, -1), 4),
            // A large exponent, 2^30.
            ["ParityLarge"] = Root(Of(parity, 1 << 30), 4),
            // A large negative exponent, -2^30.
            ["ParityLargeNegative"] = Root(Of(parity, -(1 << 30)), 4),
            // int.MaxValue.
            ["ParityMax"] = Root(Of(parity, int.MaxValue), 4),
            // int.MinValue.
            ["ParityMin"] = Root(Of(parity, int.MinValue), 4),
            // int.MinValue + 1.
            ["ParityMinPlus1"] = Root(Of(parity, int.MinValue + 1), 4),

            // NearSquares (n = 4): A = r² + d of 8 words is used as it is (t = 0), so the root is r - 1, r or r + 1
            // and the remainder spans its whole range [0, 2r]. r² - 1: the root is r - 1.
            ["NearSquareMinus1"] = Root(Of(ToWords(r * r - 1, 8), 0), 4),
            // r²: the root is r.
            ["NearSquareExact"] = Root(Of(ToWords(r * r, 8), 0), 4),
            // r² + 1.
            ["NearSquarePlus1"] = Root(Of(ToWords(r * r + 1, 8), 0), 4),
            // r² + 2r: the largest remainder.
            ["NearSquarePlus2R"] = Root(Of(ToWords(r * r + 2 * r, 8), 0), 4),
            // (r + 1)²: the root is r + 1.
            ["NearSquareNext"] = Root(Of(ToWords((r + 1) * (r + 1), 8), 0), 4),
        };

        // PrecisionLadder: every result width that changes the margin of the approximation (m = ceil(n/2)) and the
        // widths of the old fixed 4-word margin.
        foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 8, 9, 16, 17, 32, 33 })
        {
            // 3 in one word.
            cases[$"Ladder3n{n}"] = Root(Of(3u, 0), n);
            // 7·2^1: an odd exponent.
            cases[$"Ladder7n{n}"] = Root(Of(7u, 1), n);
            // An n-word value with its top bit set.
            cases[$"LadderRandomn{n}"] = Root(Of(LadderWords(n), -5), n);
        }

        // InputWidth (n = 4): operands of 1, n, 2n, 2n + 1 and 4n words with their top bit set, so A is shifted left
        // (t > 0), taken as it is (t = 0) or truncated (t < 0, with set and with zero dropped bits).
        uint[] width1 = [0xCC90_CC45u];
        uint[] width4 = [0x3490_DA35u, 0xC119_10E3u, 0x8502_1455u, 0xA9DE_B5BBu];
        uint[] width8 =
        [
            0xD09D_6B83u, 0xA4F3_041Cu, 0xF343_AB44u, 0x3D18_CAB9u, 0x6C7E_11FAu, 0x19E2_990Bu, 0x68CD_F732u,
            0x8A68_4DA0u,
        ];
        uint[] width9 =
        [
            0x5B87_0F3Cu, 0x68FA_F132u, 0xCFF7_2393u, 0x3462_E95Du, 0x26C5_E01Au, 0x1014_C08Au, 0xF7C5_3298u,
            0xEE95_1D2Bu, 0xB49E_DCDFu,
        ];
        uint[] width16 =
        [
            0xE891_F7A4u, 0xDE34_B44Fu, 0x4E0B_F4BAu, 0xA11D_9B52u, 0x7260_F957u, 0x89D0_786Au, 0x8F05_A94Fu,
            0x6A7F_0037u, 0xB083_4B86u, 0x210E_74F0u, 0x4531_9FA3u, 0xBEF1_F7ABu, 0x50DD_377Du, 0xB60D_45B8u,
            0x537A_3428u, 0x9409_24F0u,
        ];
        uint[] high =
        [
            0x4C02_28A5u, 0x3901_C1C7u, 0x76CD_C780u, 0x8C87_6E85u, 0xFC9F_0B39u, 0xBAF1_7DC7u, 0x1AC9_BD5Eu,
            0x8ED3_285Bu,
        ];
        // One word.
        cases["Width1"] = Root(Of(width1, 0), 4);
        // 4 words.
        cases["Width4"] = Root(Of(width4, 0), 4);
        // 8 words.
        cases["Width8"] = Root(Of(width8, 0), 4);
        // 9 words, with an odd exponent.
        cases["Width9"] = Root(Of(width9, 1), 4);
        // 16 words: truncated, with set bits dropped.
        cases["Width16"] = Root(Of(width16, 0), 4);
        // 16 words whose low 8 words are 0: truncated, with zero bits dropped.
        cases["Width16ZeroLow"] = Root(Of([0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, .. high], 0), 4);
        return cases;
    }

    private static readonly Dictionary<string, RootCase> s_rsqrtCases = CreateRsqrtCases();

    public static TheoryData<Engine, string> RsqrtCasesOnBothEngines()
    {
        var data = new TheoryData<Engine, string>();
        foreach (var name in s_rsqrtCases.Keys)
        {
            data.Add(Engine.Gpu, name);
            data.Add(Engine.Cpu, name);
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
    public void Rsqrt_IsRoundedTowardZero(Engine engine, string caseName)
    {
        var rootCase = s_rsqrtCases[caseName];
        var p = 32 * rootCase.ResultWords;
        var a = new Dyadic(rootCase.Value.Significand, rootCase.Value.Exponent);
        var (expectedSig, expectedExp) = BigFloatReference.RzRsqrt(a.Mantissa, a.Exponent, p);

        using var value = new GpuBigUInt(_workspace, rootCase.Value.Words);
        using var result = new GpuBigUInt(_workspace, rootCase.ResultWords);
        var context = CreateContext();
        var operand = new GpuFloatOperand(value.Handle, rootCase.Value.Exponent);
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
    [InlineData(Engine.Gpu)]
    [InlineData(Engine.Cpu)]
    public void Rsqrt_NegativeAndZeroThrow(Engine engine)
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
        // 0x123456789ABCDEF0: the significand of the ExponentParity cases.
        uint[] parity = [0x9ABC_DEF0u, 0x1234_5678u];

        var cases = new Dictionary<string, RootCase>
        {
            // 4: an exact inverse root (a power of four), even exponent.
            ["PowerOfFourEven"] = Root(Of(4u, 0), 4),
            // 2·2^1 = 4: the same with an odd exponent.
            ["PowerOfFourOdd"] = Root(Of(2u, 1), 4),
            // 2^64·2^-64 = 1 in 3 words: an exact inverse root of a wide value.
            ["PowerOfFourWide"] = Root(Of([0u, 0u, 1u], -64), 4),
            // 5 = 4 + 1: the inverse root lies just below an exact value, where a result rounded up would land on it.
            ["PowerOfFourPlus1"] = Root(Of(5u, 0), 4),
            // 2^64 + 1: the same 2^-32 below 2^-32.
            ["PowerOfFourPlus1Wide"] = Root(Of([1u, 0u, 1u], 0), 4),
            // 2: an irrational inverse root.
            ["Two"] = Root(Of(2u, 0), 4),
            // 9 in 8 words.
            ["Nine"] = Root(Of(9u, 0), 8),

            // 0xFFFFFFFF·2^1: the significand fills its word and the exponent is odd, so the value moves one word up
            // instead of losing its lowest bit (losing it would round the inverse root up).
            ["OddExponentFullWidth"] = Root(Of(0xFFFF_FFFFu, 1), 4),
            // The same with 4 words.
            ["OddExponentFullWidthWide"] = Root(Of([0x1234_5679u, 0x9ABC_DEF0u, 0x0FED_CBA9u, 0x8765_4321u], -3), 4),

            // ExponentParity (computed in long): an even exponent.
            ["Parity0"] = Root(Of(parity, 0), 4),
            // An odd exponent.
            ["Parity1"] = Root(Of(parity, 1), 4),
            // A negative odd exponent.
            ["ParityMinus1"] = Root(Of(parity, -1), 4),
            // int.MaxValue.
            ["ParityMax"] = Root(Of(parity, int.MaxValue), 4),
            // int.MinValue.
            ["ParityMin"] = Root(Of(parity, int.MinValue), 4),
            // int.MinValue + 1.
            ["ParityMinPlus1"] = Root(Of(parity, int.MinValue + 1), 4),
        };

        // PrecisionLadder: every result width that changes the margin m = max(1, n - floor(w/2)), and the widths of the
        // old fixed 4-word margin (at 5 words and more only about 16n + 65 bits were right).
        foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 8, 9, 16, 17, 32, 33 })
        {
            // 3 in one word.
            cases[$"Ladder3n{n}"] = Root(Of(3u, 0), n);
            // 7·2^1: an odd exponent.
            cases[$"Ladder7n{n}"] = Root(Of(7u, 1), n);
            // An n-word value with its top bit set.
            cases[$"LadderRandomn{n}"] = Root(Of(LadderWords(n), -5), n);
        }

        // InputWidth (n = 4): operands of 1, n, 2n, 2n + 1, 4n and 4n + 1 words with their top bit set; the radicand is
        // never truncated.
        uint[] width1 = [0xFF81_A0F7u];
        uint[] width4 = [0x1448_916Eu, 0xE0C4_F8EAu, 0x4CDE_F01Eu, 0xDD10_1E0Eu];
        uint[] width8 =
        [
            0x6F55_B23Du, 0xC6F6_DA4Eu, 0x4CB2_6DF8u, 0x1932_5503u, 0x3AAA_2665u, 0x80D4_2D42u, 0x73E6_2CC8u,
            0xB63F_7888u,
        ];
        uint[] width9 =
        [
            0xA2A6_7B84u, 0xEFF3_E302u, 0xD3F9_D904u, 0x5220_DB7Au, 0x5AED_5BBBu, 0x2FEE_64F9u, 0xC6B2_2471u,
            0x284D_9A6Cu, 0xADCA_9CDDu,
        ];
        uint[] width16 =
        [
            0x145D_A23Du, 0x9B5A_B10Cu, 0xECA4_1C13u, 0xAF69_3618u, 0xBFEF_DCD2u, 0xAEB0_70C4u, 0x1867_7801u,
            0x7CF3_8D69u, 0x416C_B32Fu, 0x1180_FDCBu, 0x6245_D3B3u, 0x6C78_EFB3u, 0xA6E1_2E8Cu, 0xFA87_85C0u,
            0xEE13_59AEu, 0xF9F1_2D61u,
        ];
        uint[] width17 =
        [
            0x9A7B_3B5Du, 0x4DC4_7BD9u, 0x4D36_CA62u, 0xDDCC_BA53u, 0xE474_3C98u, 0x264F_4A9Fu, 0xD1E8_B9A9u,
            0x2386_0859u, 0x2414_031Cu, 0x3BB6_1B79u, 0x5CEB_6C37u, 0x26E0_FE8Cu, 0x7DA0_100Bu, 0x5516_BACCu,
            0x4393_6283u, 0x927A_B5CDu, 0xF0F0_5932u,
        ];
        // One word.
        cases["Width1"] = Root(Of(width1, 0), 4);
        // 4 words.
        cases["Width4"] = Root(Of(width4, 0), 4);
        // 8 words, with an odd exponent.
        cases["Width8"] = Root(Of(width8, 1), 4);
        // 9 words.
        cases["Width9"] = Root(Of(width9, 0), 4);
        // 16 words, with an odd exponent.
        cases["Width16"] = Root(Of(width16, 1), 4);
        // 17 words.
        cases["Width17"] = Root(Of(width17, 0), 4);
        return cases;
    }

    // The sqrt or 1/sqrt of value, rounded to the given number of result words.
    private static RootCase Root(Operand value, int words) => new(value, words);

    // The n-word operand of the PrecisionLadder cases, with its top bit set (only its width matters).
    private static uint[] LadderWords(int n) => n switch
    {
        1 => [0xFCF2_6FF5u],
        2 => [0xD9C2_DDA9u, 0xEE58_0941u],
        3 => [0xB693_4B5Du, 0x43E7_5488u, 0xE7FD_0332u],
        4 => [0x9363_C911u, 0x9976_BFD0u, 0x7BD5_4C32u, 0xCD95_4454u],
        5 => [0x7034_36C6u, 0xEF06_1B17u, 0x0FAD_7531u, 0xB082_A879u, 0xE032_1914u],
        6 => [0x4D04_A47Au, 0x4495_765Fu, 0xA385_BE30u, 0x9370_0C9Eu, 0xC654_4D12u, 0xACD7_ED65u],
        8 =>
        [
            0x06A5_8FE2u, 0xEFB4_3CEEu, 0xCB36_102Fu, 0x594A_C4E8u, 0x9298_A50Du, 0x8E1C_5F19u, 0xBA26_7309u,
            0x9C8D_61D9u,
        ],
        9 =>
        [
            0xE375_FD96u, 0x4543_9836u, 0x5F0E_592Eu, 0x3C38_290Du, 0x78BA_D90Bu, 0x7EBE_87F3u, 0xA4B3_3843u,
            0x098E_75E6u, 0xD88B_9B84u,
        ],
        16 =>
        [
            0xED29_1D84u, 0x9C2F_272Au, 0x69F7_D829u, 0x70B5_B610u, 0xC3AA_14FBu, 0x132D_F5EBu, 0x0E8C_5CD6u,
            0x8495_E241u, 0x0666_C4DEu, 0xADD8_DAA7u, 0x3E67_7740u, 0x3D40_8059u, 0xEAB6_D738u, 0xDB35_8396u,
            0x2CB9_A04Fu, 0xE3AE_C099u,
        ],
        17 =>
        [
            0xC9F9_9B38u, 0xF1BE_8272u, 0xFDD0_0128u, 0x53A3_1A35u, 0xA9CC_48F9u, 0x03D0_3EC5u, 0xF919_1210u,
            0x7196_F64Eu, 0xD618_51A2u, 0x7A36_9E2Bu, 0x1F52_8330u, 0xD7BF_FF10u, 0x40CF_EAAAu, 0x22C1_AA10u,
            0xAEB4_534Du, 0xF811_FA02u, 0xDDCE_3E82u,
        ],
        32 =>
        [
            0xBA30_48C6u, 0xF524_FBA4u, 0xA77B_481Du, 0x9F8B_A860u, 0x25CC_F4D6u, 0x1D51_538Fu, 0xB758_1071u,
            0x54A7_0312u, 0x0180_5119u, 0x73B4_22F1u, 0x4D18_763Du, 0xE537_1BC5u, 0x4C48_9E60u, 0x53F7_AB34u,
            0x4C64_5033u, 0xA9E1_672Du, 0xD891_E42Cu, 0x41D5_69FEu, 0x5B43_F96Bu, 0xA8AD_5625u, 0x52C4_4E58u,
            0x2229_4904u, 0x9118_AB8Fu, 0x52F3_F1B3u, 0x53D1_BC80u, 0x2DF5_90F5u, 0x9EC1_6654u, 0xEC48_B685u,
            0xC3F9_D858u, 0x2598_40C7u, 0x83A9_A30Au, 0xCDC5_C21Du,
        ],
        33 =>
        [
            0x9700_B67Bu, 0x4AB4_56EBu, 0x3B53_711Cu, 0x8279_0C85u, 0x0BEF_28D4u, 0x0DF3_8C69u, 0xA1E4_D5ABu,
            0x41A8_171Fu, 0xD131_EDDCu, 0x4011_E676u, 0x2E03_822Du, 0x7FB6_8A7Cu, 0xA261_A1D2u, 0x9B83_C1AEu,
            0xCE5F_0331u, 0x3E44_9096u, 0x2505_626Bu, 0xCB88_E83Bu, 0xF5B6_0D75u, 0x90EB_AA7Cu, 0xAA6E_88C9u,
            0xA57B_C8C3u, 0xE588_C3B0u, 0xA96F_E908u, 0x3FCE_DC63u, 0x3EE2_AF7Du, 0xD468_9702u, 0x40AA_5B7Eu,
            0x9F1E_3815u, 0xD06E_6915u, 0x7565_FE6Fu, 0x8E52_79E3u, 0xA6E0_0C23u,
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(n), n, "No ladder operand of this width."),
    };

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
        // A 9-word significand with its top bit set and a 1-word one; only their widths matter (the seeded sweep covers
        // random values).
        uint[] wide =
        [
            0xCC90_CC45u, 0x3490_DA35u, 0xC119_10E3u, 0x8502_1455u, 0xA9DE_B5BBu, 0xD09D_6B83u, 0xA4F3_041Cu,
            0xF343_AB44u, 0xBD18_CAB9u,
        ];
        uint[] narrow = [0xEC7E_11FAu];
        uint[] allOnes = [uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue];
        uint[] threeInNineWords = [3u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u];
        uint[] oneInNineWords = [1u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u];

        return new Dictionary<string, ArithmeticCase>
        {
            // 3 + 5: an exact small sum.
            ["AddSmallIntegers"] = Add(Of(3u, 0), Of(5u, 0)),
            // 2^100 + 2^-100: far exponents; the aligned sum has 201 bits.
            ["AddFarExponents"] = Add(Pow2(100), Pow2(-100), 4),
            // 2^100 - 2^-100: a borrow through every word (all ones below 2^100).
            ["AddBorrowThroughEveryWord"] = Add(Pow2(100), Of(1u, -100, isNegative: true), 4),
            // wide - wide: a sum that cancels to zero.
            ["AddCancelsToZero"] = Add(Of(wide, -3), Of(wide, -3, isNegative: true), 4),
            // (2^128 - 1) + 1/2: a sum that stays below a carry.
            ["AddAllOnesPlusHalf"] = Add(Of(allOnes, 0), Pow2(-1), 4),
            // wide + narrow·2^17: operands of different widths.
            ["AddWideAndNarrow"] = Add(Of(wide, 0), Of(narrow, 17), 4),

            // 3 · 5: an exact small product.
            ["MultiplySmallIntegers"] = Mul(Of(3u, 0), Of(5u, 0)),
            // (2^128 - 1)²: a 256-bit product truncated to 128 bits.
            ["MultiplyAllOnes"] = Mul(Of(allOnes, 0), Of(allOnes, 0), 4),
            // wide · (-narrow·2^-5): a negative product of operands of different widths.
            ["MultiplyWideByNarrow"] = Mul(Of(wide, 0), Of(narrow, -5, isNegative: true), 8),
            // 3·2^-100 · 7·2^100: exponents that cancel.
            ["MultiplyFarExponents"] = Mul(Of(3u, -100), Of(7u, 100)),

            // 1 / 3 in one word.
            ["DivideOneByThree1"] = Div(Of(1u, 0), Of(3u, 0)),
            // 1 / 3 in 4 words.
            ["DivideOneByThree4"] = Div(Of(1u, 0), Of(3u, 0), 4),
            // 1 / 3 in 8 words.
            ["DivideOneByThree8"] = Div(Of(1u, 0), Of(3u, 0), 8),
            // narrow / wide in one word: bits(R) - bits(L) > p, where a shift of 2p left fewer than p quotient bits.
            ["DivideNarrowByWide1"] = Div(Of(narrow, 0), Of(wide, 0)),
            // narrow / wide in 4 words: the same with p = 128.
            ["DivideNarrowByWide4"] = Div(Of(narrow, 0), Of(wide, 0), 4),
            // wide / narrow: the reverse.
            ["DivideWideByNarrow"] = Div(Of(wide, 0), Of(narrow, 0), 4),
            // wide·2^7 / wide·2^7: a value over itself.
            ["DivideSameValue"] = Div(Of(wide, 7), Of(wide, 7), 4),
            // 1 / 3 with the 3 stored in 9 words: a divisor with leading zero words.
            ["DivideByThreeInNineWords"] = Div(Of(1u, 0), Of(threeInNineWords, 0), 4),
            // 2^-100 / (-3·2^100): far exponents and a negative divisor.
            ["DivideFarExponents"] = Div(Pow2(-100), Of(3u, 100, isNegative: true), 4),

            // 1 / 3 in one word.
            ["ReciprocalOfThree1"] = Rcp(Of(3u, 0)),
            // 1 / 3 in 4 words.
            ["ReciprocalOfThree4"] = Rcp(Of(3u, 0), 4),
            // 1 / 3 in 8 words: with 2v + 2 working words, 8 result words had fewer than p bits.
            ["ReciprocalOfThree8"] = Rcp(Of(3u, 0), 8),
            // 1 / wide.
            ["ReciprocalOfWide"] = Rcp(Of(wide, 0), 4),
            // 1 / 3 with the 3 stored in 9 words: the result must not depend on the leading zero words.
            ["ReciprocalOfThreeInNineWords"] = Rcp(Of(threeInNineWords, 0), 4),
            // 1 / (-5·2^-7): a negative scaled value.
            ["ReciprocalNegativeScaled"] = Rcp(Of(5u, -7, isNegative: true), 4),

            // The significand 1: floor(2^T / 1) = 2^T needs T + 1 bits, so a working width of exactly T bits failed on
            // the GPU (the device correction did not converge).
            // mutant: GSharpKernelFloatProvider.cs:260 `reciprocalWords * 32 - 1` -> `reciprocalWords * 32 - 0` (the
            // four ReciprocalOfOne* cases on the GPU, and ReciprocalOfOneNearMin in the limit cases)
            // 1 / 2^3 in one word.
            ["ReciprocalOfOne1"] = Rcp(Pow2(3)),
            // 1 / 1 in 4 words.
            ["ReciprocalOfOne4"] = Rcp(Pow2(0), 4),
            // 1 / 1 in 9 words.
            ["ReciprocalOfOne9"] = Rcp(Pow2(0), 9),
            // 1 / 1 with the 1 stored in 9 words.
            ["ReciprocalOfOneInNineWords"] = Rcp(Of(oneInNineWords, 0), 4),
        };
    }

    // 2^exp: the significand 1 in one word.
    private static Operand Pow2(int exp) => new([1u], exp);

    // ±significand·2^exp in one word.
    private static Operand Of(uint significand, int exp, bool isNegative = false) =>
        new([significand], exp, isNegative);

    // ±words·2^exp with the words as given (leading zero words kept).
    private static Operand Of(uint[] words, int exp, bool isNegative = false) => new(words, exp, isNegative);

    // left + right, rounded to the given number of result words.
    private static ArithmeticCase Add(Operand left, Operand right, int words = 1) =>
        new(FloatOperation.Add, left, right, words);

    // left · right, rounded to the given number of result words.
    private static ArithmeticCase Mul(Operand left, Operand right, int words = 1) =>
        new(FloatOperation.Multiply, left, right, words);

    // left / right, rounded to the given number of result words.
    private static ArithmeticCase Div(Operand left, Operand right, int words = 1) =>
        new(FloatOperation.Divide, left, right, words);

    // 1 / value, rounded to the given number of result words.
    private static ArithmeticCase Rcp(Operand value, int words = 1) =>
        new(FloatOperation.Reciprocal, value, null, words);

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

    private static IGpuBigFloatArithmetic Arithmetic(Engine engine) => engine switch
    {
        Engine.Gpu => GSharpKernelFloatProvider.Instance,
        Engine.Cpu => CSharpCpuBigFloatArithmetic.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(engine), engine, "Unknown engine."),
    };
}
