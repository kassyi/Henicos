#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Accuracy tests of the big-float <c>Pow</c> of the GPU provider (<see cref="GSharpKernelFloatProvider"/>) and the CPU
/// provider (<see cref="CSharpCpuBigFloatArithmetic"/>).
/// </summary>
/// <remarks>
/// <para>
/// Every expected value comes from <see cref="BigFloatIntervalOracle.PowRz"/> and the input that the test holds, never
/// from a value read back from the GPU. The result has <c>p = 32·n</c> bits for <c>n</c> result words.
/// </para>
/// <para>
/// Contract (stage 6 of #628): the result is <c>RZ_p(x^y)</c> for <c>x &gt; 0</c> (P1). A power that is exactly a
/// <c>p</c>-bit value is found before any approximation and returned as it is (no attempt); every other power goes
/// through <c>e^(y·ln x)</c> and the rounding check (Ziv), which retries with more guard words while the bound straddles
/// a <c>p</c>-bit boundary. P5: the approximation of <c>e^(y·L)</c> lies within its bound, the Exp bound plus the
/// error that the bound of <c>L ≈ ln x</c> carries into the argument, of <c>x^y</c> itself.
/// </para>
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
[Collection(TestCollections.GlobalStateSequential)]
public class GpuBigFloatPowTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBigFloatPowTests()
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

    private static readonly Dictionary<string, PowInput> s_cases = CreateCases();

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

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in s_cases.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    // The exactly representable powers.
    public static TheoryData<string, string> ExactCasesOnBothEngines()
    {
        var data = new TheoryData<string, string>();
        foreach (var name in s_cases.Keys)
        {
            if (name.StartsWith("PowExact/", StringComparison.Ordinal))
            {
                data.Add("Gpu", name);
                data.Add("Cpu", name);
            }
        }

        return data;
    }

    // Every case that computes an approximation: not exact, and |y·ln x| not below 2^-(p+1).
    public static TheoryData<string, string> ApproximationCasesOnBothEngines()
    {
        var data = new TheoryData<string, string>();
        foreach (var name in s_cases.Keys)
        {
            if (!name.StartsWith("PowExact/", StringComparison.Ordinal) && !name.StartsWith("PowTinyShortcut/", StringComparison.Ordinal))
            {
                data.Add("Gpu", name);
                data.Add("Cpu", name);
            }
        }

        return data;
    }

    // P1: the result is RZ_p(x^y), the significand normalized to the top bit of the result words.
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(CasesOnBothEngines))]
    // mutant: BigFloatTranscendentalMath.cs PowApproximation / GpuBigFloatTranscendentals.cs ComputePow shortcut
    // `top <= -(resultBits + 1)` -> `top <= -(resultBits - 2)`: PowTiny/3^(±2^-127) (RZ is 1 + 2^-127 and 1 - 3·2^-128,
    // not a shortcut value)
    // mutant: BigFloatTranscendentalMath.cs Pow `IsNegative ? LargestBelowOne(resultBits) : One(resultBits)` swapped /
    // GpuBigFloatTranscendentals.cs Pow `IsNegative ? WriteLargestBelowOne(...) : WriteOne(...)` swapped: PowTinyShortcut
    // mutant: BigFloatTranscendentalMath.cs PowApproximation `yNegative != log.IsNegative` -> `yNegative` /
    // GpuBigFloatTranscendentals.cs ComputePow `y.IsNegative != log.IsNegative` -> `y.IsNegative` (the sign of ln x
    // dropped): every x < 1, e.g. PowControl/0.1^3.7, PowLargeArgument/0.75^(10^9), PowNearOne/(1-2^-150)^(3*2^100)
    public void Pow_IsRz(string engine, string caseName)
    {
        var powCase = s_cases[caseName];
        var p = 32 * powCase.Words;

        var (rSig, result) = RunPow(Arithmetic(engine), powCase);

        var expected = BigFloatIntervalOracle.PowRz(
            powCase.XSignificand, powCase.XExponent, powCase.YSignificand, powCase.YExponent, powCase.YNegative, p);
        Assert.False(result.IsNegative);
        Assert.True(
            (rSig, (long)result.Exponent) == expected,
            $"{caseName} [{engine}]: 0x{rSig:X} * 2^{result.Exponent} is not RZ(x^y) = 0x{expected.Significand:X} * 2^{expected.Exponent}");
    }

    // An exactly representable power is returned before any approximation: no attempt of the rounding check (which
    // would never accept a value on a p-bit boundary).
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ExactCasesOnBothEngines))]
    public void Pow_ExactPower_ComputesNoApproximation(string engine, string caseName)
    {
        var powCase = s_cases[caseName];

        var (sig, exponent, attempts) = RunPowWithAttempts(engine, powCase);

        Assert.Equal(0, attempts);
        var expected = BigFloatIntervalOracle.PowRz(
            powCase.XSignificand, powCase.XExponent, powCase.YSignificand, powCase.YExponent, powCase.YNegative,
            32 * powCase.Words);
        Assert.Equal(expected, (sig, (long)exponent));
    }

    // P5: the approximation of e^(y·L) lies within its bound (Exp bound plus the argument error |y|·B_L) of x^y.
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ApproximationCasesOnBothEngines))]
    // Not killed: BigFloatTranscendentalMath.cs PowApproximation `exp.ErrorBound + argumentBound` -> `exp.ErrorBound`
    // (D dropped). With the Log precision of the plan (W + 64 bits) D is 1, below the Exp bound; the formula of D is
    // checked by TranscendentalPlannerTests and, with a short Log where D dominates, by BigFloatTranscendentalMathTests.
    public void Pow_ApproximationIsWithinBound(string engine, string caseName)
    {
        var powCase = s_cases[caseName];

        var approximation = PowApproximation(engine, powCase);

        Assert.Equal(approximation.WorkingBits + 32, (int)approximation.Significand.GetBitLength());
        Assert.True(
            BigFloatIntervalOracle.IsPowWithin(
                powCase.XSignificand, powCase.XExponent, powCase.YSignificand, powCase.YExponent, powCase.YNegative,
                approximation.Significand, approximation.Exponent, approximation.ErrorBound, approximation.UnitExponent,
                approximation.WorkingBits + 96),
            $"{caseName} [{engine}]: x^y is more than {approximation.ErrorBound} units from the approximation");
    }

    // The GPU and the CPU provider take the same exact check, the same plans and the same integer steps.
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(CaseNames))]
    public void Pow_CpuAndGpuAgree(string caseName)
    {
        var powCase = s_cases[caseName];

        var gpu = RunPowWithAttempts("Gpu", powCase);
        var cpu = RunPowWithAttempts("Cpu", powCase);

        Assert.Equal(cpu, gpu);
    }

    // The inputs of PowRetryInputs lie closer to a p-bit value than the first approximation resolves: the rounding check
    // rejects it and accepts a later one, after the same number of attempts on the GPU as on the CPU
    // (BigFloatTranscendentalMathTests checks the CPU half without a GPU).
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(PowRetryInputs.Names), MemberType = typeof(PowRetryInputs))]
    // mutant: GpuBigFloatTranscendentals.cs Pow `approximation.ErrorBound + argumentBound` -> `0` (no bound: the first
    // approximation is accepted): every case
    public void ZivRetry_RetriesAndIsRz_OnGpu(string caseName)
    {
        var powCase = PowRetryInputs.Cases[caseName];

        var gpu = RunPowWithAttempts("Gpu", powCase);
        var cpu = RunPowWithAttempts("Cpu", powCase);

        Assert.True(gpu.Attempts >= 2, $"{caseName}: the first approximation was accepted on the GPU");
        Assert.Equal(cpu, gpu);
        var expected = BigFloatIntervalOracle.PowRz(
            powCase.XSignificand, powCase.XExponent, powCase.YSignificand, powCase.YExponent, powCase.YNegative,
            32 * powCase.Words);
        Assert.Equal(expected, (gpu.Significand, (long)gpu.Exponent));
    }

    // The base must be positive, whatever the exponent (y = 0 included): a negative base and zero throw, as before.
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    // mutant: BigFloatTranscendentalMath.cs Pow / GpuBigFloatTranscendentals.cs Pow zero check `if (xSignificand.IsZero)`
    // -> `if (false)`: 0^3 and 0^0 return a value (0^-1 reaches the approximation and does not end)
    // mutant: CSharpCpuBigFloatArithmetic.cs Pow negative-base `throw` removed: the Cpu cases with a negative base (the
    // host Pow ignores the sign, so (-2)^3 would return 8)
    [InlineData("Gpu", "0", false, "3", false, "zero")]
    [InlineData("Cpu", "0", false, "3", false, "zero")]
    [InlineData("Gpu", "0", false, "0", false, "zero")]
    [InlineData("Cpu", "0", false, "0", false, "zero")]
    [InlineData("Gpu", "0", false, "1", true, "zero")]
    [InlineData("Cpu", "0", false, "1", true, "zero")]
    [InlineData("Gpu", "2", true, "3", false, "negative")]
    [InlineData("Cpu", "2", true, "3", false, "negative")]
    [InlineData("Gpu", "2", true, "0", false, "negative")]
    [InlineData("Cpu", "2", true, "0", false, "negative")]
    public void Pow_OutsideDomain_Throws(
        string engine, string xSignificand, bool xNegative, string ySignificand, bool yNegative, string word)
    {
        var arithmetic = Arithmetic(engine);
        using var x = new GpuBigUInt(_workspace, ToWords(BigInteger.Parse(xSignificand)));
        using var y = new GpuBigUInt(_workspace, ToWords(BigInteger.Parse(ySignificand)));
        using var result = new GpuBigUInt(_workspace, 4);

        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            var context = CreateContext();
            arithmetic.Pow(
                ref context, new GpuFloatOperand(x.Handle, 0, xNegative), new GpuFloatOperand(y.Handle, 0, yNegative),
                result.Handle);
        });
        Assert.Contains(word, error.Message, StringComparison.Ordinal);
        Assert.Contains("Pow", error.Message, StringComparison.Ordinal);
    }

    // A result exponent outside int throws OverflowException, named Pow, an overflow for x^y > 1 and an underflow for
    // x^y < 1: on the exact path (2^y, 0.5^y), before the approximation (|y·ln x| >= 2^32, or the planner's range), and
    // after the series (the exponent of the result, one past int).
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    // mutant: BigFloatTranscendentalMath.cs TryExactPow `OutOfRange("Pow", negative: resultExponent < 0)` ->
    // `resultExponent > 0` (the names swapped): 2^(2^31+127), 2^(-2^31+126), 0.5^(2^31+127)
    // mutant: BigFloatTranscendentalMath.cs TryExactPow `negative: (xTwos < 0) != yNegative` -> `negative: xTwos < 0`
    // (y >= 2^63): 2^(-2^70)
    // mutant: BigFloatTranscendentalMath.cs PowApproximation / GpuBigFloatTranscendentals.cs ComputePow
    // `_ = TranscendentalPlanner.ExpReduction(..., "Pow")` removed (PlanExp then throws with the Exp name): 3^(2^31)
    // mutant: BigFloatTranscendentalMath.cs / GpuBigFloatTranscendentals.cs Pow `ToResultExponent(exponent, "Pow")` ->
    // `ToExpResultExponent(exponent)` (an Exp message): the after-series cases
    // mutant: TranscendentalPlanner.cs OutOfRange `$"{operation} overflow: ..."` -> `$"Exp overflow: ..."`: the
    // overflow cases
    // Not killed: BigFloatTranscendentalMath.cs PowApproximation `if (top > 32)` throw removed; ExpReduction rejects
    // |y·L| >= 2^32 with the same message, so the check only spares the double conversion.
    [InlineData("Gpu", "PowRange/2^(2^31+127)", "overflow")]
    [InlineData("Cpu", "PowRange/2^(2^31+127)", "overflow")]
    [InlineData("Gpu", "PowRange/2^(-2^31+126)", "underflow")]
    [InlineData("Cpu", "PowRange/2^(-2^31+126)", "underflow")]
    [InlineData("Gpu", "PowRange/0.5^(2^31+127)", "underflow")]
    [InlineData("Cpu", "PowRange/0.5^(2^31+127)", "underflow")]
    [InlineData("Gpu", "PowRange/2^(2^70)", "overflow")]
    [InlineData("Cpu", "PowRange/2^(2^70)", "overflow")]
    [InlineData("Gpu", "PowRange/2^(-2^70)", "underflow")]
    [InlineData("Cpu", "PowRange/2^(-2^70)", "underflow")]
    [InlineData("Gpu", "PowRange/0.5^(2^70)", "underflow")]
    [InlineData("Cpu", "PowRange/0.5^(2^70)", "underflow")]
    [InlineData("Gpu", "PowRange/3^(2^40)", "overflow")]
    [InlineData("Cpu", "PowRange/3^(2^40)", "overflow")]
    [InlineData("Gpu", "PowRange/3^(-2^40)", "underflow")]
    [InlineData("Cpu", "PowRange/3^(-2^40)", "underflow")]
    [InlineData("Gpu", "PowRange/0.75^(2^40)", "underflow")]
    [InlineData("Cpu", "PowRange/0.75^(2^40)", "underflow")]
    [InlineData("Gpu", "PowRange/3^(2^31)", "overflow")]
    [InlineData("Cpu", "PowRange/3^(2^31)", "overflow")]
    [InlineData("Gpu", "PowRange/3^y,after-series", "overflow")]
    [InlineData("Cpu", "PowRange/3^y,after-series", "overflow")]
    [InlineData("Gpu", "PowRange/3^-y,after-series", "underflow")]
    [InlineData("Cpu", "PowRange/3^-y,after-series", "underflow")]
    public void Pow_OutOfRange_Throws(string engine, string caseName, string word)
    {
        var powCase = s_rangeCases[caseName];

        var error = Assert.Throws<OverflowException>(() => RunPow(Arithmetic(engine), powCase));
        Assert.Contains("Pow " + word, error.Message, StringComparison.Ordinal);
    }

    private static readonly Dictionary<string, PowInput> s_rangeCases = CreateRangeCases();

    // The approximation is defined for a base other than 1: Pow returns 1^y = 1 before it, and ln 1 = 0 has no
    // approximation to multiply (on the GPU only a placeholder).
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    // mutant: GpuBigFloatTranscendentals.cs ComputePow `if (log.IsZero)` -> `if (false)` / BigFloatTranscendentalMath.cs
    // PowApproximation `if (log.Significand.IsZero)` -> `if (false)` (no exception)
    [InlineData("Gpu")]
    [InlineData("Cpu")]
    public void PowApproximation_OfBaseOne_Throws(string engine)
    {
        var one = new PowInput(BigInteger.One << 40, -40, 3, 0, false, 4);

        var error = Assert.Throws<ArgumentException>(() => PowApproximation(engine, one));
        Assert.Contains("base other than 1", error.Message, StringComparison.Ordinal);
    }

    private (BigInteger Significand, GpuFloatResult Result) RunPow(IGpuBigFloatArithmetic arithmetic, PowInput powCase)
    {
        using var x = new GpuBigUInt(_workspace, ToWords(powCase.XSignificand));
        using var y = new GpuBigUInt(_workspace, ToWords(powCase.YSignificand));
        using var result = new GpuBigUInt(_workspace, powCase.Words);
        ResetOptions();
        var context = CreateContext();

        var r = arithmetic.Pow(
            ref context, new GpuFloatOperand(x.Handle, powCase.XExponent),
            new GpuFloatOperand(y.Handle, powCase.YExponent, powCase.YNegative), result.Handle);
        return (result.ToBigInteger(), r);
    }

    private (BigInteger Significand, int Exponent, int Attempts) RunPowWithAttempts(string engine, PowInput powCase)
    {
        ResetOptions();
        if (engine == "Cpu")
        {
            var (significand, exponent) = BigFloatTranscendentalMath.Pow(
                powCase.XSignificand, powCase.XExponent, powCase.YSignificand, powCase.YExponent, powCase.YNegative,
                32 * powCase.Words, _workspace.Transcendental, out var cpuAttempts);
            return (significand, exponent, cpuAttempts);
        }

        using var x = new GpuBigUInt(_workspace, ToWords(powCase.XSignificand));
        using var y = new GpuBigUInt(_workspace, ToWords(powCase.YSignificand));
        using var result = new GpuBigUInt(_workspace, powCase.Words);
        var gpu = GpuBigFloatTranscendentals.Pow(
            _workspace, new GpuFloatOperand(x.Handle, powCase.XExponent),
            new GpuFloatOperand(y.Handle, powCase.YExponent, powCase.YNegative), result.Handle, out var gpuAttempts);
        Assert.False(gpu.IsNegative);
        return (result.ToBigInteger(), gpu.Exponent, gpuAttempts);
    }

    private TranscendentalApproximation PowApproximation(string engine, PowInput powCase)
    {
        ResetOptions();
        if (engine == "Cpu")
        {
            return BigFloatTranscendentalMath.PowApproximation(
                powCase.XSignificand, powCase.XExponent, powCase.YSignificand, powCase.YExponent, powCase.YNegative,
                32 * powCase.Words, _workspace.Transcendental);
        }

        using var x = new GpuBigUInt(_workspace, ToWords(powCase.XSignificand));
        using var y = new GpuBigUInt(_workspace, ToWords(powCase.YSignificand));
        return GpuBigFloatTranscendentals.PowApproximation(
            _workspace, new GpuFloatOperand(x.Handle, powCase.XExponent),
            new GpuFloatOperand(y.Handle, powCase.YExponent, powCase.YNegative), powCase.Words);
    }

    private void ResetOptions()
    {
        var options = _workspace.Transcendental;
        options.ExpHalvingCount = null;
        options.SeriesBlockSize = null;
        options.AtanhHornerMaxTerms = 8;
    }

    private GpuBigIntExecutionContext CreateContext() =>
        new(_workspace, _workspace.MemoryManager, new LegacyProviderArithmeticAdapter(), _workspace.Stream);

    private static IGpuBigFloatArithmetic Arithmetic(string engine) =>
        engine == "Gpu" ? GSharpKernelFloatProvider.Instance : CSharpCpuBigFloatArithmetic.Instance;

    private static uint[] ToWords(BigInteger value)
    {
        var words = new uint[Math.Max(1, (int)((value.GetBitLength() + 31) / 32))];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = (uint)(value >> (32 * i) & uint.MaxValue);
        }

        return words;
    }

    private static PowInput Case(BigInteger x, int xExp, BigInteger y, int yExp, bool yNegative = false, int words = 4) =>
        new(x, xExp, y, yExp, yNegative, words);

    private static Dictionary<string, PowInput> CreateCases()
    {
        var one = BigInteger.One;
        var cases = new Dictionary<string, PowInput>
        {
            // Exact powers (the examples of the design: 2^3, 2^1000, 4^0.5, (9/4)^0.5, 2^-3), with one per branch of the
            // exact check: x = 2^b (integer and root exponents), odd x with an integer exponent up to exactly p bits,
            // roots of perfect squares and fourth powers, negative exponents of powers of two, x = 1 and y = 0.
            ["PowExact/2^3"] = Case(2, 0, 3, 0),
            ["PowExact/2^1000"] = Case(2, 0, 1000, 0),
            ["PowExact/4^0.5"] = Case(4, 0, 1, -1),
            ["PowExact/(9/4)^0.5"] = Case(9, -2, 1, -1),
            ["PowExact/2^-3"] = Case(2, 0, 3, 0, yNegative: true),
            ["PowExact/0.75^2"] = Case(3, -2, 2, 0),
            ["PowExact/3^80/n=4"] = Case(3, 0, 80, 0),
            ["PowExact/3^20/n=1"] = Case(3, 0, 20, 0, words: 1),
            ["PowExact/81^0.75"] = Case(81, 0, 3, -2),
            ["PowExact/(3^80)^0.5/n=2"] = Case(BigInteger.Pow(3, 80), 0, 1, -1, words: 2),
            ["PowExact/64^-1.5"] = Case(64, 0, 3, -1, yNegative: true),
            ["PowExact/(2^6)^0.5"] = Case(one << 6, 0, 1, -1),
            ["PowExact/1^(2^-100)"] = Case(one << 40, -40, 1, -100),
            ["PowExact/1^3"] = Case(1, 0, 3, 0),
            ["PowExact/1^-2.5"] = Case(1, 0, 5, -1, yNegative: true),
            ["PowExact/1^(2^1000)"] = Case(1, 0, 1, 1000),
            ["PowExact/3^0"] = Case(3, 0, 0, 0),
            ["PowExact/(2^-50)^0"] = Case(1, -50, 0, 0, yNegative: true),

            // The largest and smallest exact powers whose exponent fits int (p = 128: 2^E = 2^127 · 2^(E - 127)).
            ["PowExact/2^(2^31+126)"] = Case(2, 0, 2147483774, 0),
            ["PowExact/2^(-2^31+127)"] = Case(2, 0, 2147483521, 0, yNegative: true),

            // Roots that are not dyadic: x = 2^b with an odd b, odd parts that are not squares (also at the third
            // square root of 81^(1/8)), and 2^(3/4).
            ["PowNonIntegerRoot/2^0.5"] = Case(2, 0, 1, -1),
            ["PowNonIntegerRoot/3^0.5"] = Case(3, 0, 1, -1),
            ["PowNonIntegerRoot/12^0.5"] = Case(12, 0, 1, -1),
            ["PowNonIntegerRoot/18^0.5"] = Case(18, 0, 1, -1),
            ["PowNonIntegerRoot/8^0.25"] = Case(8, 0, 1, -2),
            ["PowNonIntegerRoot/81^0.125"] = Case(81, 0, 1, -3),
            ["PowNonIntegerRoot/10^0.25"] = Case(10, 0, 1, -2),

            // Rational powers that are not dyadic.
            ["PowNonDyadicRational/3^-1"] = Case(3, 0, 1, 0, yNegative: true),
            ["PowNonDyadicRational/3^-2"] = Case(3, 0, 2, 0, yNegative: true),
            ["PowNonDyadicRational/(9/4)^-0.5"] = Case(9, -2, 1, -1, yNegative: true),
            ["PowNonDyadicRational/81^-0.25"] = Case(81, 0, 1, -2, yNegative: true),

            // Dyadic powers longer than p: S^y one bit past p (3^81: 129 bits, 3^21: 34 bits, 3^41: 65 bits). The
            // inputs of PowRetryInputs (near-exact powers, (2^100 + 1)^1) are added below.
            ["PowLongerThanP/3^81/n=4"] = Case(3, 0, 81, 0),
            ["PowLongerThanP/3^21/n=1"] = Case(3, 0, 21, 0, words: 1),
            ["PowLongerThanP/(3^82)^0.5/n=2"] = Case(BigInteger.Pow(3, 82), 0, 1, -1, words: 2),


            // |y·ln x| next to 2^-(p+1): 2^-200 and 2^-129·ln 2 take the shortcut (RZ is 1, or 1 - 2^-128 for a
            // negative y·ln x, also from x < 1); 3^(±2^-127) do not (RZ is 1 + 2^-127 and 1 - 3·2^-128).
            ["PowTinyShortcut/3^(2^-200)"] = Case(3, 0, 1, -200),
            ["PowTinyShortcut/3^(-2^-200)"] = Case(3, 0, 1, -200, yNegative: true),
            ["PowTinyShortcut/0.75^(2^-200)"] = Case(3, -2, 1, -200),
            ["PowTinyShortcut/2^(2^-129)"] = Case(2, 0, 1, -129),
            ["PowTiny/3^(2^-127)"] = Case(3, 0, 1, -127),
            ["PowTiny/3^(-2^-127)"] = Case(3, 0, 1, -127, yNegative: true),

            // Near 1 in x: ln x far below the fixed point of the series, and |y·ln x| about 2^-50 from y = 2^100 (with
            // y = 3, (1 - 2^-150)^3 takes the shortcut: RZ is 1 - 2^-128).
            ["PowNearOne/(1+2^-150)^(2^100)"] = Case((one << 150) + 1, -150, 1, 100),
            ["PowNearOne/(1-2^-150)^(3*2^100)"] = Case((one << 150) - 1, -150, 3, 100),
            ["PowTinyShortcut/(1-2^-150)^3"] = Case((one << 150) - 1, -150, 3, 0),
            ["PowNearOne/(1+2^-1000)^(2^999)"] = Case((one << 1000) + 1, -1000, 1, 999),

            // Large |y·ln x|: results with exponents up to the edge of int.
            ["PowLargeArgument/3^(10^8)"] = Case(3, 0, 100_000_000, 0),
            ["PowLargeArgument/2^(2^30+0.5)"] = Case(2, 0, (one << 31) + 1, -1),
            ["PowLargeArgument/0.75^(10^9)"] = Case(3, -2, 1_000_000_000, 0),
            ["PowLargeArgument/3^-1000.5"] = Case(3, 0, 2001, -1, yNegative: true),
            ["PowRangeEdge/3^y,exponent=int.MaxValue"] = Case(3, 0, 22731641363684458, -24),
            ["PowRangeEdge/3^-y,exponent=int.MinValue"] = Case(3, 0, 22731638675032290, -24, yNegative: true),
        };

        foreach (var (name, input) in PowRetryInputs.Cases)
        {
            cases[name] = input;
        }

        // Doubles: 1.5^2.75, 10^0.3, 0.1^3.7, 123.456^-1.25, 2^100.5, 0.75^-300.125.
        foreach (var (x, y) in new[] { (1.5, 2.75), (10.0, 0.3), (0.1, 3.7), (123.456, -1.25), (2.0, 100.5), (0.75, -300.125) })
        {
            var (xSig, xExp) = FromDouble(x);
            var (ySig, yExp) = FromDouble(Math.Abs(y));
            cases[FormattableString.Invariant($"PowControl/{x}^{y}")] = Case(xSig, xExp, ySig, yExp, y < 0);
        }

        // Long operands: an 8-word x near 1.x and a 4-word y near 5.x (seeded).
        var random = new Random(628);
        cases["PowLongOperands/seed628"] = Case(RandomOdd(random, 256), -255, RandomOdd(random, 128), -125);

        // Precision ladder for 3^0.5 and 3^-0.5: word boundaries.
        foreach (var words in new[] { 1, 2, 5, 31 })
        {
            cases[$"PowPrecisionLadder/3^0.5/n={words}"] = Case(3, 0, 1, -1, words: words);
            cases[$"PowPrecisionLadder/3^-0.5/n={words}"] = Case(3, 0, 1, -1, yNegative: true, words: words);
        }

        return cases;
    }

    private static Dictionary<string, PowInput> CreateRangeCases()
    {
        var one = BigInteger.One;
        return new Dictionary<string, PowInput>
        {
            // Exact: 2^E has the exponent E - 127 at p = 128.
            ["PowRange/2^(2^31+127)"] = Case(2, 0, 2147483775, 0),
            ["PowRange/2^(-2^31+126)"] = Case(2, 0, 2147483522, 0, yNegative: true),
            ["PowRange/0.5^(2^31+127)"] = Case(1, -1, 2147483775, 0),

            // Exact with y >= 2^63: |b·y| is out of range without forming y.
            ["PowRange/2^(2^70)"] = Case(2, 0, 1, 70),
            ["PowRange/2^(-2^70)"] = Case(2, 0, 1, 70, yNegative: true),
            ["PowRange/0.5^(2^70)"] = Case(1, -1, 1, 70),

            // |y·ln x| >= 2^32, and 2^31·ln 3 (below 2^32, beyond the planner's range).
            ["PowRange/3^(2^40)"] = Case(3, 0, 1, 40),
            ["PowRange/3^(-2^40)"] = Case(3, 0, 1, 40, yNegative: true),
            ["PowRange/0.75^(2^40)"] = Case(3, -2, 1, 40),
            ["PowRange/3^(2^31)"] = Case(3, 0, one << 31, 0),

            // y·log2 3 = 2^31 + 127.5 and -(2^31 - 126.5) up to 2^-26: the planner's early check passes, the result
            // exponent is 2^31 and -2^31 - 1.
            ["PowRange/3^y,after-series"] = Case(3, 0, 22731641374269703, -24),
            ["PowRange/3^-y,after-series"] = Case(3, 0, 22731638685617535, -24, yNegative: true),
        };
    }

    private static (BigInteger Significand, int Exponent) FromDouble(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;
        return (exponent == 0 ? mantissa : mantissa | (1L << 52), (exponent == 0 ? 1 : exponent) - 1075);
    }

    // An odd value with exactly `bits` bits.
    private static BigInteger RandomOdd(Random random, int bits)
    {
        var bytes = new byte[bits / 8];
        random.NextBytes(bytes);
        bytes[^1] |= 0x80;
        bytes[0] |= 1;
        return new BigInteger(bytes, isUnsigned: true);
    }
}
