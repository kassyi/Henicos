#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Accuracy tests of the big-float <c>Log</c> and <c>Exp</c> (and later <c>Pow</c>) of the GPU provider
/// (<see cref="GSharpKernelFloatProvider"/>) and the CPU provider (<see cref="CSharpCpuBigFloatArithmetic"/>).
/// </summary>
/// <remarks>
/// <para>
/// Every expected value comes from <see cref="BigFloatIntervalOracle"/> and the input that the test holds
/// (significand and exponent), never from a value read back from the GPU. The result has <c>p = 32·n</c> bits for
/// <c>n</c> result words.
/// </para>
/// <para>
/// Contract checked here (from stage 5 of #628): the result is <c>RZ_p(v)</c>, the exact value rounded toward zero to
/// <c>p</c> bits, with the significand normalized to the top bit of the result words (P1). The rounding check (Ziv)
/// accepts the approximation only when every value within its bound truncates to the same <c>p</c> bits, and retries
/// with more guard words otherwise; the inputs of <see cref="ZivRetryInputs"/> need that retry.
/// </para>
/// <para>
/// P5: the approximation before rounding (<c>W</c> bits for Log, <c>e^x</c> at <c>W + 32</c> bits for Exp) lies within
/// its planned bound <c>B</c> of the exact value, in units of its last bit at <c>W</c> bits. The bounds are those of
/// the design §10.1 (atanh Horner <c>B = 7</c>, atanh Paterson–Stockmeyer <c>B = m + 6</c>, exp Paterson–Stockmeyer
/// <c>B = ceil(5.83·2^s) − 1</c>). With 32 guard bits P1 rarely sees an error of a few units; P5 does.
/// </para>
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
[Collection(TestCollections.GlobalStateSequential)]
public class GpuBigFloatTranscendentalTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBigFloatTranscendentalTests()
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
    /// Named Log inputs <c>x = Significand · 2^Exponent</c> with the number of result words and optional series
    /// parameters.
    /// </summary>
    private static readonly Dictionary<string, LogCase> s_logCases = CreateLogCases();

    public static TheoryData<string, string> LogCasesOnBothEngines()
    {
        var data = new TheoryData<string, string>();
        foreach (var name in s_logCases.Keys)
        {
            data.Add("Gpu", name);
            data.Add("Cpu", name);
        }

        return data;
    }

    public static TheoryData<string> LogCaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in s_logCases.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    // P1: the result is RZ_p(ln x) (oracle: e^R <= x < e^(R + ulp) for ln x > 0, mirrored for ln x < 0).
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(LogCasesOnBothEngines))]
    // mutant: the e·ln 2 term dropped (#628 B2): GpuBigFloatTranscendentals.cs SumWithLn2 `MultiplyLn2(...)` removed /
    // BigFloatTranscendentalMath.cs Log `var sum = MultiplyLn2(...)` -> `var sum = BigInteger.Zero`
    // (LogLargeArgument, LogPowerOfTwo, LogControl)
    // mutant: m in [1, 2) instead of [3/4, 3/2): `var upper = bitLength >= 2 && ... == 3;` -> `var upper = false;` in
    // both files (LogWorstY 0.75·2^k, LogPrecisionLadder)
    // mutant: y in fixed point instead of with its own exponent: GpuBigFloatTranscendentals.cs EvaluateSeries
    // `workingBits + ratio.BitLength - ratio.ReducedBits + 1` -> `workingBits + 1` / BigFloatTranscendentalMath.cs
    // `workingBits + bitLength - reducedBits + 1` -> `workingBits + 1` (LogNearOne)
    public void Log_IsRz(string engine, string caseName)
    {
        var logCase = s_logCases[caseName];
        var words = logCase.Words;

        var (rSig, result) = RunLog(Arithmetic(engine), logCase);

        Assert.True(rSig.IsZero || rSig.GetBitLength() == 32 * words, $"{caseName}: significand not normalized");
        Assert.True(
            BigFloatIntervalOracle.IsRzOfLog(
                logCase.Significand, logCase.Exponent, rSig, result.Exponent, result.IsNegative, 32 * words),
            $"{caseName} [{engine}]: {Describe(rSig, result)} is not RZ(ln x)");
    }

    // P5: the W-bit approximation lies within its bound B of ln x (oracle: e^(A - B) <= x <= e^(A + B)).
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(LogCasesOnBothEngines))]
    // mutant: TranscendentalPlanner.cs LogPatersonStockmeyerErrorBound `blockSize + 6` -> `blockSize + 1` (B below the
    // error the series reaches: LogSeriesForms 1+2^-10)
    // mutant: BigFloatTranscendentalMath.cs AtanhSeriesPatersonStockmeyer top block `i < terms - top` ->
    // `i < terms - top - 1` / GpuBigFloatTranscendentals.cs `i = 1; i < terms - top` -> `i < terms - top - 1` (the last
    // term dropped): LogSeriesForms
    // mutant: GpuBigFloatTranscendentals.cs / BigFloatTranscendentalMath.cs AtanhSeriesPatersonStockmeyer lower blocks
    // `2 * (first + i) + 1` -> `2 * (first + i) + 3`: LogSeriesForms, LogWorstY
    public void Log_ApproximationIsWithinBound(string engine, string caseName)
    {
        var logCase = s_logCases[caseName];

        var approximation = LogApproximation(engine, logCase);

        if (approximation.Significand.IsZero)
        {
            // ln 1 = 0 exactly.
            Assert.Equal(0, new Dyadic(logCase.Significand, logCase.Exponent).CompareTo(Dyadic.One));
            return;
        }

        Assert.Equal(approximation.WorkingBits, (int)approximation.Significand.GetBitLength());
        Assert.True(
            BigFloatIntervalOracle.IsLogWithin(
                logCase.Significand, logCase.Exponent, approximation.Significand, approximation.Exponent,
                approximation.IsNegative, approximation.ErrorBound, approximation.UnitExponent,
                approximation.WorkingBits + 64),
            $"{caseName} [{engine}]: ln x is more than {approximation.ErrorBound} units from the approximation");
    }

    // The GPU and the CPU provider run the same planner and the same integer steps, so they agree bit for bit.
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(LogCaseNames))]
    public void Log_CpuAndGpuAgree(string caseName)
    {
        var logCase = s_logCases[caseName];

        var gpu = RunLog(GSharpKernelFloatProvider.Instance, logCase);
        var cpu = RunLog(CSharpCpuBigFloatArithmetic.Instance, logCase);

        Assert.Equal(cpu, gpu);
    }

    // The ZivRetry inputs (ZivRetryInputs) lie next to a p-bit boundary, closer than the error bound of the first
    // approximation, so the rounding check rejects it, retries with more guard words and then returns RZ_p(v). The GPU
    // needs the same number of attempts as the CPU (BigFloatTranscendentalMathTests checks the CPU half without a GPU)
    // and returns the same bits.
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ZivRetryInputs.Names), MemberType = typeof(ZivRetryInputs))]
    // mutant: GpuBigFloatTranscendentals.cs TryTruncate `ErrorBound > guard || guard + ErrorBound >= limit` ->
    // `ErrorBound > guard` (upper half dropped): LogBelow, LogAbove, ExpBelow, ExpAbove; -> `guard + ErrorBound >= limit`
    // (lower half dropped): LogNegativeBelow, LogNegativeAbove, LogDistanceAbove, ExpDistanceAbove
    public void ZivRetry_RetriesAndIsRz_OnGpu(string caseName)
    {
        var (kind, sig, exp, negative) = ZivRetryInputs.Cases[caseName];
        using var x = new GpuBigUInt(_workspace, ToWords(sig));
        using var gpuResult = new GpuBigUInt(_workspace, 4);
        var options = _workspace.Transcendental;
        ApplyOptions(null, null, 8);
        var operand = new GpuFloatOperand(x.Handle, exp, negative);

        int gpuAttempts;
        int cpuAttempts;
        GpuFloatResult gpu;
        (BigInteger Significand, long Exponent, bool IsNegative) cpu;
        if (kind == "Log")
        {
            gpu = GpuBigFloatTranscendentals.Log(_workspace, operand, gpuResult.Handle, out gpuAttempts);
            var (s, e, n) = BigFloatTranscendentalMath.Log(sig, exp, 128, options, out cpuAttempts);
            cpu = (s, e, n);
            var gpuSig = gpuResult.ToBigInteger();
            Assert.True(BigFloatIntervalOracle.IsRzOfLog(sig, exp, gpuSig, gpu.Exponent, gpu.IsNegative, 128),
                $"{caseName}: {Describe(gpuSig, gpu)} is not RZ(ln x)");
        }
        else
        {
            gpu = GpuBigFloatTranscendentals.Exp(_workspace, operand, gpuResult.Handle, out gpuAttempts);
            var (s, e) = BigFloatTranscendentalMath.Exp(sig, exp, negative, 128, options, out cpuAttempts);
            cpu = (s, e, false);
            var (num, denShift) = BigFloatIntervalOracle.ToRatio(sig, exp);
            Assert.Equal(BigFloatIntervalOracle.ExpRz(negative ? -num : num, denShift, 128), (gpuResult.ToBigInteger(), (long)gpu.Exponent));
        }

        Assert.True(gpuAttempts >= 2, $"{caseName}: the first approximation was accepted on the GPU");
        Assert.Equal(cpuAttempts, gpuAttempts);
        Assert.Equal((cpu.Significand, cpu.Exponent, cpu.IsNegative), (gpuResult.ToBigInteger(), (long)gpu.Exponent, gpu.IsNegative));
    }

    // ExpReductionEdge reaches r >= 2 ln 2: the double quotient of x / ln 2 rounds below 94867, so k = 94865 (coverage
    // witness of the branch where Q = e^r · 2^W has W + 3 bits).
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ExpReductionEdge_ReducesByTwoBelowTheMultiple()
    {
        var expCase = s_expCases["ExpReductionEdge/94867ln2+2^-200"];
        var bits = (int)expCase.Significand.GetBitLength();
        var leading = (ulong)(expCase.Significand >> (bits - 64));
        var xApprox = BigFloatTranscendentalMath.ApproximateArgument(leading, expCase.Exponent + bits, false);

        var plan = TranscendentalPlanner.PlanExp(128, xApprox, new GpuBigFloatTranscendentalOptions());

        Assert.Equal(94865, plan.Reduction);
    }

    // ln 1 = 0 exactly: a cleared result with exponent 0.
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Gpu")]
    [InlineData("Cpu")]
    public void Log_OfOne_IsZero(string engine)
    {
        var (rSig, result) = RunLog(Arithmetic(engine), new LogCase(BigInteger.One << 40, -40, 4));

        Assert.Equal(BigInteger.Zero, rSig);
        Assert.Equal(new GpuFloatResult(0, false), result);
    }

    // ln 0 and the logarithm of a negative number are undefined.
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Gpu")]
    [InlineData("Cpu")]
    public void Log_OutsideDomain_Throws(string engine)
    {
        var arithmetic = Arithmetic(engine);
        using var zero = new GpuBigUInt(_workspace, [0u, 0u]);
        using var one = new GpuBigUInt(_workspace, [1u]);
        using var result = new GpuBigUInt(_workspace, 4);

        var zeroError = Assert.Throws<InvalidOperationException>(() =>
        {
            var context = CreateContext();
            arithmetic.Log(ref context, new GpuFloatOperand(zero.Handle, 0), result.Handle);
        });
        Assert.Contains("zero", zeroError.Message, StringComparison.Ordinal);

        var negativeError = Assert.Throws<InvalidOperationException>(() =>
        {
            var context = CreateContext();
            arithmetic.Log(ref context, new GpuFloatOperand(one.Handle, 0, true), result.Handle);
        });
        Assert.Contains("negative", negativeError.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Named Exp inputs <c>x = ±Significand · 2^Exponent</c> with the number of result words and an optional
    /// <see cref="GpuBigFloatTranscendentalOptions.ExpHalvingCount"/>.
    /// </summary>
    private static readonly Dictionary<string, ExpCase> s_expCases = CreateExpCases();

    public static TheoryData<string, string> ExpCasesOnBothEngines()
    {
        var data = new TheoryData<string, string>();
        foreach (var name in s_expCases.Keys)
        {
            data.Add("Gpu", name);
            data.Add("Cpu", name);
        }

        return data;
    }

    // Every Exp case that computes an approximation (not the tiny shortcut |x| < 2^-(p+1)).
    public static TheoryData<string, string> ExpApproximationCasesOnBothEngines()
    {
        var data = new TheoryData<string, string>();
        foreach (var (name, expCase) in s_expCases)
        {
            if ((long)expCase.Exponent + (long)expCase.Significand.GetBitLength() > -(32 * expCase.Words + 1))
            {
                data.Add("Gpu", name);
                data.Add("Cpu", name);
            }
        }

        return data;
    }

    public static TheoryData<string> ExpCaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in s_expCases.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    // P1: the result is RZ_p(e^x) (oracle: the interval of e^x truncates to one p-bit value).
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ExpCasesOnBothEngines))]
    // mutant: TranscendentalPlanner.cs PlanExp `Math.Floor(xApprox / Ln2) - 1` -> `+ 1` (r < 0): every case
    // mutant: TranscendentalPlanner.cs PlanExp `var guardWords = GuardWordsFor(errorBound);` -> `var guardWords = 1;`
    // (one guard word while the bound has up to 2^200): ExpForcedHalving s = 40, 200 (also P5 and the planner tests)
    // mutant: GpuBigFloatTranscendentals.cs / BigFloatTranscendentalMath.cs Exp tiny shortcut `-(resultBits + 1)` ->
    // `-(resultBits - 2)`: ExpTiny ±3·2^-128
    // mutant: GpuBigFloatTranscendentals.cs / BigFloatTranscendentalMath.cs Exp one squaring dropped
    // (`i < plan.Halvings` -> `i < plan.Halvings - 1`): every case outside the tiny shortcut
    // mutant: BigFloatTranscendentalMath.cs TryTruncate `Exponent + excess + guardBits` -> `Exponent + guardBits` /
    // GpuBigFloatTranscendentals.cs TryTruncate copy offset `4 * (lowWords + guardWords)` -> `4 * guardWords` (the 32
    // low bits of the Exp approximation not skipped): every case outside the shortcut
    public void Exp_IsRz(string engine, string caseName)
    {
        var expCase = s_expCases[caseName];
        var p = 32 * expCase.Words;

        var (rSig, result) = RunExp(Arithmetic(engine), expCase);

        var (num, denShift) = BigFloatIntervalOracle.ToRatio(expCase.Significand, expCase.Exponent);
        var (rzSig, rzExp) = BigFloatIntervalOracle.ExpRz(expCase.Negative ? -num : num, denShift, p);
        Assert.False(result.IsNegative);
        Assert.True(
            (rSig, (long)result.Exponent) == (rzSig, rzExp),
            $"{caseName} [{engine}]: {Describe(rSig, result)} is not RZ(e^x) = 0x{rzSig:X} * 2^{rzExp}");
    }

    // P5: the approximation e^x at W + 32 bits lies within its bound B, in units of its last bit at W bits, of e^x
    // (oracle: interval of e^x inside [A - B, A + B]). The tiny shortcut computes no approximation and is left out.
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ExpApproximationCasesOnBothEngines))]
    // mutant: TranscendentalPlanner.cs ExpPatersonStockmeyerErrorBound `583 *` -> `100 *` (B below the error the
    // squarings amplify): ExpSeriesForms, ExpForcedHalving, ExpPrecisionLadder, ExpLargeArgument
    // mutant: TranscendentalPlanner.cs PlanExp `+ BitLength(reduction) + 2` -> `+ 2` (|k|·|L - ln 2| too large):
    // ExpRangeEdge, ExpNearMultipleOfLn2 10^6, ExpNegative -1000 and -2^20
    // mutant: GpuBigFloatTranscendentals.cs / BigFloatTranscendentalMath.cs ExpSeriesPatersonStockmeyer top block
    // `i >= 1` -> `i >= 2` (the lowest step of the top block skipped): ExpSeriesForms, ExpLargeArgument
    // mutant: GpuBigFloatTranscendentals.cs / BigFloatTranscendentalMath.cs ExpSeriesPatersonStockmeyer lower blocks
    // `(first + i)` -> `(first + i + 1)` (wrong divisor in Smith's order): ExpSeriesForms, ExpLargeArgument
    public void Exp_ApproximationIsWithinBound(string engine, string caseName)
    {
        var expCase = s_expCases[caseName];

        var approximation = ExpApproximation(engine, expCase);

        var (num, denShift) = BigFloatIntervalOracle.ToRatio(expCase.Significand, expCase.Exponent);
        Assert.Equal(approximation.WorkingBits + 32, (int)approximation.Significand.GetBitLength());
        Assert.True(
            BigFloatIntervalOracle.IsExpWithin(
                expCase.Negative ? -num : num, denShift, approximation.Significand, approximation.Exponent,
                approximation.ErrorBound, approximation.UnitExponent, approximation.WorkingBits + 96),
            $"{caseName} [{engine}]: e^x is more than {approximation.ErrorBound} units from the approximation");
    }

    // The GPU and the CPU provider run the same planner and the same integer steps, so they agree bit for bit.
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(ExpCaseNames))]
    public void Exp_CpuAndGpuAgree(string caseName)
    {
        var expCase = s_expCases[caseName];

        var gpu = RunExp(GSharpKernelFloatProvider.Instance, expCase);
        var cpu = RunExp(CSharpCpuBigFloatArithmetic.Instance, expCase);

        Assert.Equal(cpu, gpu);
    }

    // exp(0) = 1 with the significand normalized to the top bit (the CPU provider used to return an unnormalized 1).
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    // mutant: BigFloatTranscendentalMath.cs Exp, zero argument `(BigInteger.One << (resultBits - 1), 1 - resultBits)` ->
    // `(BigInteger.One, 0)` (the old unnormalized 1)
    [InlineData("Gpu", false)]
    [InlineData("Cpu", false)]
    [InlineData("Gpu", true)]
    [InlineData("Cpu", true)]
    public void Exp_OfZero_IsNormalizedOne(string engine, bool negative)
    {
        var (rSig, result) = RunExp(Arithmetic(engine), new ExpCase(0, 0, negative, 4, null));

        Assert.Equal(BigInteger.One << 127, rSig);
        Assert.Equal(new GpuFloatResult(-127, false), result);
    }

    // A result exponent outside int throws OverflowException; below it the message names the underflow.
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    // Not killed: GpuBigFloatTranscendentals.cs / BigFloatTranscendentalMath.cs Exp `if (top > 32)` throw removed; the
    // planner rejects |x| >= 2^32 as well (its guard is killed in TranscendentalPlannerTests), so the check only spares
    // the planning.
    [InlineData("Gpu", "1", 32, false, "overflow")]
    [InlineData("Cpu", "1", 1000, false, "overflow")]
    [InlineData("Gpu", "1", 1000, true, "underflow")]
    [InlineData("Cpu", "1", 2000000000, true, "underflow")]
    [InlineData("Cpu", "1", 32, false, "overflow")]
    [InlineData("Gpu", "1500000000", 0, false, "overflow")]
    [InlineData("Cpu", "1500000000", 0, false, "overflow")]
    [InlineData("Gpu", "1", 32, true, "underflow")]
    [InlineData("Cpu", "1", 32, true, "underflow")]
    [InlineData("Gpu", "1500000000", 0, true, "underflow")]
    [InlineData("Cpu", "1500000000", 0, true, "underflow")]
    public void Exp_OutOfRange_Throws(string engine, string significand, int exponent, bool negative, string word)
    {
        var arithmetic = Arithmetic(engine);
        var expCase = new ExpCase(BigInteger.Parse(significand), exponent, negative, 4, null);

        var error = Assert.Throws<OverflowException>(() => RunExp(arithmetic, expCase));
        Assert.Contains(word, error.Message, StringComparison.Ordinal);
    }

    // Next to the range edge the planner cannot decide the exponent from k alone: it lets the series run, and the
    // exponent of the assembled result decides. Overflow: x / ln 2 = 2^31 - 1 + p + 0.499 gives the exponent
    // 2^31; underflow: x / ln 2 = -2^31 + p - 1.999 gives -2^31 - 1 (p = 128). The planner accepts both.
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    // mutant: TranscendentalPlanner.cs ToExpResultExponent `exponent > int.MaxValue ? throw ExpOutOfRange(negative: false)`
    // -> `exponent > int.MaxValue ? throw ExpOutOfRange(negative: true)` (the overflow named an underflow)
    [InlineData("Gpu", false, "overflow")]
    [InlineData("Cpu", false, "overflow")]
    [InlineData("Gpu", true, "underflow")]
    [InlineData("Cpu", true, "underflow")]
    public void Exp_RangeEdge_ThrowsAfterTheSeries(string engine, bool negative, string word)
    {
        var significand = negative ? 1524246680138L : 1524246860068L;
        var expCase = new ExpCase(significand, -10, negative, 4, null);

        // The planner's early check passes (the argument is exact as a double): k + 1 - p = 2^31 - 1 for the
        // overflow, k + 4 - p = -2^31 + 1 for the underflow.
        var planned = TranscendentalPlanner.PlanExp(128, (negative ? -significand : significand) / 1024.0, new());
        Assert.Equal(negative ? -2147483523L : 2147483774L, planned.Reduction);

        var error = Assert.Throws<OverflowException>(() => RunExp(Arithmetic(engine), expCase));
        Assert.Contains(word, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(negative ? "overflow" : "underflow", error.Message, StringComparison.Ordinal);
    }

    private (BigInteger Significand, GpuFloatResult Result) RunExp(IGpuBigFloatArithmetic arithmetic, ExpCase expCase)
    {
        using var x = new GpuBigUInt(_workspace, ToWords(expCase.Significand));
        using var result = new GpuBigUInt(_workspace, expCase.Words);
        ApplyOptions(expCase.Halvings, expCase.BlockSize, 8);
        var context = CreateContext();

        var r = arithmetic.Exp(ref context, new GpuFloatOperand(x.Handle, expCase.Exponent, expCase.Negative),
            result.Handle);
        return (result.ToBigInteger(), r);
    }

    private TranscendentalApproximation ExpApproximation(string engine, ExpCase expCase)
    {
        ApplyOptions(expCase.Halvings, expCase.BlockSize, 8);
        if (engine == "Cpu")
        {
            return BigFloatTranscendentalMath.ExpApproximation(
                expCase.Significand, expCase.Exponent, expCase.Negative, 32 * expCase.Words, _workspace.Transcendental);
        }

        using var x = new GpuBigUInt(_workspace, ToWords(expCase.Significand));
        return GpuBigFloatTranscendentals.ExpApproximation(
            _workspace, new GpuFloatOperand(x.Handle, expCase.Exponent, expCase.Negative), expCase.Words);
    }

    private (BigInteger Significand, GpuFloatResult Result) RunLog(IGpuBigFloatArithmetic arithmetic, LogCase logCase)
    {
        using var x = new GpuBigUInt(_workspace, ToWords(logCase.Significand));
        using var result = new GpuBigUInt(_workspace, logCase.Words);
        ApplyOptions(null, logCase.BlockSize, logCase.HornerMaxTerms);
        var context = CreateContext();

        var r = arithmetic.Log(ref context, new GpuFloatOperand(x.Handle, logCase.Exponent), result.Handle);
        return (result.ToBigInteger(), r);
    }

    private TranscendentalApproximation LogApproximation(string engine, LogCase logCase)
    {
        ApplyOptions(null, logCase.BlockSize, logCase.HornerMaxTerms);
        if (engine == "Cpu")
        {
            return BigFloatTranscendentalMath.LogApproximation(
                logCase.Significand, logCase.Exponent, 32 * logCase.Words, _workspace.Transcendental);
        }

        using var x = new GpuBigUInt(_workspace, ToWords(logCase.Significand));
        return GpuBigFloatTranscendentals.LogApproximation(
            _workspace, new GpuFloatOperand(x.Handle, logCase.Exponent), logCase.Words);
    }

    private void ApplyOptions(int? halvings, int? blockSize, int hornerMaxTerms)
    {
        var options = _workspace.Transcendental;
        options.ExpHalvingCount = halvings;
        options.SeriesBlockSize = blockSize;
        options.AtanhHornerMaxTerms = hornerMaxTerms;
    }

    private GpuBigIntExecutionContext CreateContext() =>
        new(_workspace, _workspace.MemoryManager, new LegacyProviderArithmeticAdapter(), _workspace.Stream);

    private static IGpuBigFloatArithmetic Arithmetic(string engine) =>
        engine == "Gpu" ? GSharpKernelFloatProvider.Instance : CSharpCpuBigFloatArithmetic.Instance;

    private static string Describe(BigInteger sig, GpuFloatResult result) =>
        $"{(result.IsNegative ? "-" : "+")}0x{sig:X} * 2^{result.Exponent}";

    private static uint[] ToWords(BigInteger value)
    {
        var words = new uint[Math.Max(1, (int)((value.GetBitLength() + 31) / 32))];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = (uint)(value >> (32 * i) & uint.MaxValue);
        }

        return words;
    }

    /// <summary>An Exp input <c>±Significand · 2^Exponent</c>.</summary>
    private readonly record struct ExpCase(
        BigInteger Significand, int Exponent, bool Negative, int Words, int? Halvings, int? BlockSize = null);

    /// <summary>A Log input <c>Significand · 2^Exponent</c>.</summary>
    private readonly record struct LogCase(
        BigInteger Significand, int Exponent, int Words, int? BlockSize = null, int HornerMaxTerms = 8);

    private static Dictionary<string, ExpCase> CreateExpCases()
    {
        var cases = new Dictionary<string, ExpCase>();

        // Control.
        foreach (var x in new[] { 1, 2, 100, 300 })
        {
            cases[$"ExpControl/{x}"] = new ExpCase(x, 0, false, 4, null);
        }

        // e^x >= 2^448 overflowed the old 14-word fixed point (#628 B3); 1000 needs more than the old 1000 terms.
        foreach (var words in new[] { 4, 8 })
        {
            foreach (var x in new[] { 311, 320, 400, 1000 })
            {
                cases[$"ExpLargeArgument/{x}/n={words}"] = new ExpCase(x, 0, false, words, null);
            }

            cases[$"ExpLargeArgument/2^20/n={words}"] = new ExpCase(1, 20, false, words, null);
        }

        // Negative x takes the same reduction (r > 0) instead of 1 / e^|x|.
        cases["ExpNegative/-1.5"] = new ExpCase(3, -1, true, 4, null);
        foreach (var x in new[] { 100, 320, 1000 })
        {
            cases[$"ExpNegative/-{x}"] = new ExpCase(x, 0, true, 4, null);
        }

        cases["ExpNegative/-2^20"] = new ExpCase(1, 20, true, 4, null);

        // Tiny arguments: 2^-200 and (1 - 2^-64)·2^-129 take the shortcut (|x| < 2^-(p+1)); 2^-129 = 2^-(p+1) exactly,
        // 2^-128, 3·2^-129 and 3·2^-128 do not. RZ(e^(±3·2^-128)) is not a shortcut value (1 + 1 ulp above 1, 1 - 3 ulp
        // below 1), so a shortcut that also took it would return a wrong value.
        foreach (var negative in new[] { false, true })
        {
            var sign = negative ? "-" : "+";
            cases[$"ExpTiny/{sign}2^-200"] = new ExpCase(1, -200, negative, 4, null);
            cases[$"ExpTiny/{sign}(1-2^-64)*2^-129"] = new ExpCase(ulong.MaxValue, -129 - 64, negative, 4, null);
            cases[$"ExpTiny/{sign}2^-129"] = new ExpCase(1, -129, negative, 4, null);
            cases[$"ExpTiny/{sign}2^-128"] = new ExpCase(1, -128, negative, 4, null);
            cases[$"ExpTiny/{sign}3*2^-129"] = new ExpCase(3, -129, negative, 4, null);
            cases[$"ExpTiny/{sign}3*2^-128"] = new ExpCase(3, -128, negative, 4, null);
        }

        // x next to k·ln 2 at p + 64 bits: r at the ends of its interval.
        foreach (var k in new[] { 1, 1_000_000 })
        {
            var integerBits = (int)new BigInteger(k).GetBitLength();
            var fractionBits = 128 + 64 - integerBits;
            var below = k * Ln2Floor(fractionBits + 32) >> 32;
            cases[$"ExpNearMultipleOfLn2/{k}ln2-"] = new ExpCase(below, -fractionBits, false, 4, null);
            cases[$"ExpNearMultipleOfLn2/{k}ln2+"] = new ExpCase(below + 1, -fractionBits, false, 4, null);
        }

        // Forced halving counts: the 2^s growth of the error and the guard words that follow it.
        foreach (var halvings in new[] { 2, 3, 13, 14, 40, 200 })
        {
            cases[$"ExpForcedHalving/3/s={halvings}"] = new ExpCase(3, 0, false, 4, halvings);
        }

        // Precision ladder: the planner picks s = 2, 2 and 5 with one guard word (Paterson–Stockmeyer). n = 2 and 50
        // have the series remainder eps_R closest to its limit 1 (review of stages 1-4).
        foreach (var words in new[] { 1, 2, 31, 50, 129 })
        {
            cases[$"ExpPrecisionLadder/3/n={words}"] = new ExpCase(3, 0, false, words, null);
        }

        // Series forms: the Paterson–Stockmeyer block size 2, 3, the planner's choice and ceil(sqrt N) + 3, with the
        // planner's s and with s = 7.
        foreach (var (significand, exponent, negative, label) in new[] { (3, 0, false, "3"), (3, -1, true, "-1.5") })
        {
            foreach (var words in new[] { 4, 32 })
            {
                foreach (var halvings in new int?[] { null, 7 })
                {
                    var x = (negative ? -significand : significand) * Math.ScaleB(1, exponent);
                    var terms = TranscendentalPlanner.PlanExp(
                        32 * words, x, new GpuBigFloatTranscendentalOptions { ExpHalvingCount = halvings }).Terms;
                    foreach (var blockSize in new int?[] { 2, 3, null, (int)Math.Ceiling(Math.Sqrt(terms)) + 3 })
                    {
                        cases[$"ExpSeriesForms/{label}/n={words}/s={halvings?.ToString() ?? "auto"}/m={blockSize?.ToString() ?? "auto"}"] =
                            new ExpCase(significand, exponent, negative, words, halvings, blockSize);
                    }
                }
            }
        }

        // x = 94867·ln 2 + 2^-200: the double quotient x / ln 2 rounds below 94867, k = 94865 and r = 2 ln 2 + 2^-200, so
        // e^r >= 4 and Q has W + 3 bits (review of stages 1-4; other such k: 95901, 189407, 189734, 190061, 191802,
        // 192129, 782427606).
        cases["ExpReductionEdge/94867ln2+2^-200"] = new ExpCase((94867 * Ln2Floor(232) >> 32) + 1, -200, false, 4, null);

        // The largest reduction error: x is k·L + R0·2^(Lw - Wr) + (2^(Lw - Wr) - 1) in Lw fraction bits plus 40 one
        // bits below 2^-Lw, so truncating x, L and r all lose almost a full unit in the same direction (δ near 1.5u).
        cases["ExpLargestReductionError/k=1000"] = LargestReductionErrorCase(1000, 4);

        // The largest arguments whose result exponent still fits in int (about +-(2^31 + 127)·ln 2).
        cases["ExpRangeEdge/+1488522000"] = new ExpCase(1488522000, 0, false, 4, null);
        cases["ExpRangeEdge/-1488522000"] = new ExpCase(1488522000, 0, true, 4, null);

        return cases;
    }

    // The input for k (r = 1, R0 = 2^Wr) built from the plan of x ≈ k ln 2 + 1 and the host ln 2 constant (verified
    // against the oracle in GpuBigFloatConstantsTests); the expected value still comes from the oracle.
    private static ExpCase LargestReductionErrorCase(int k, int words)
    {
        var plan = TranscendentalPlanner.PlanExp(32 * words, k * Math.Log(2) + 1.0, new GpuBigFloatTranscendentalOptions());
        if (plan.Reduction != k)
        {
            throw new InvalidOperationException($"the plan reduces by {plan.Reduction}, not {k}");
        }

        var ln2Bits = (plan.Ln2FractionBits + 31) / 32 * 32;
        var dropped = ln2Bits - (plan.WorkingBits - plan.Halvings);
        var fixedPoint = k * Kassyi.GSharp.Stdlib.GpuMath.BigNum.Constants.GpuBigFloatConstants.GetLn2Fixed(ln2Bits)
                         + (BigInteger.One << (plan.WorkingBits - plan.Halvings + dropped))
                         + (BigInteger.One << dropped) - 1;
        var significand = (fixedPoint << 40) + (BigInteger.One << 40) - 1;
        return new ExpCase(significand, -(ln2Bits + 40), false, words, null);
    }

    // floor(ln 2 · 2^bits) from ln 2 = Σ 1 / (j 2^j) (independent of the product); the remainder after bits + 8
    // terms is below 2^-(bits + 8), so the floor is at most one unit low.
    private static BigInteger Ln2Floor(int bits)
    {
        var scale = BigInteger.One << (bits + 16);
        BigInteger sum = 0;
        for (var j = 1; j <= bits + 16; j++)
        {
            sum += scale / (j * (BigInteger.One << j));
        }

        return sum >> 16;
    }

    private static Dictionary<string, LogCase> CreateLogCases()
    {
        var cases = new Dictionary<string, LogCase>();
        var one = BigInteger.One;

        // Control: e = 1..6, |y| <= 1/5.
        cases["LogControl/2"] = new LogCase(2, 0, 4);
        cases["LogControl/3"] = new LogCase(3, 0, 4);
        cases["LogControl/100"] = new LogCase(100, 0, 4);

        // Large and small arguments: the e·ln 2 term (#628 B2: the unreduced series saturates at about 8.18).
        cases["LogLargeArgument/1000"] = new LogCase(1000, 0, 4);
        cases["LogLargeArgument/2^40"] = new LogCase(1, 40, 4);
        cases["LogLargeArgument/2^100"] = new LogCase(1, 100, 4);
        cases["LogLargeArgument/2^-40"] = new LogCase(1, -40, 4);
        cases["LogLargeArgument/2^-100"] = new LogCase(1, -100, 4);
        cases["LogLargeArgument/3*2^-1000"] = new LogCase(3, -1000, 4);

        // Powers of two: m = 1, u = 0, ln x = e·ln 2 alone.
        foreach (var words in new[] { 1, 4 })
        {
            cases[$"LogPowerOfTwo/2^1/n={words}"] = new LogCase(1, 1, words);
            cases[$"LogPowerOfTwo/2^-1/n={words}"] = new LogCase(1, -1, words);
            cases[$"LogPowerOfTwo/2^31/n={words}"] = new LogCase(one << 7, 24, words);
            cases[$"LogPowerOfTwo/2^-1000/n={words}"] = new LogCase(1, -1000, words);
        }

        // The ends of m in [3/4, 3/2): |y| = 1/5 - O(2^-p) just below 3/2, |y| = 1/7 at 3/4.
        foreach (var words in new[] { 4, 32 })
        {
            var p = 32 * words;
            foreach (var k in new[] { 0, 9 })
            {
                cases[$"LogWorstY/(1.5-2^-{p})*2^{k}/n={words}"] = new LogCase((3 * (one << (p - 1))) - 1, k - p, words);
                cases[$"LogWorstY/0.75*2^{k}/n={words}"] = new LogCase(3, k - 2, words);
            }
        }

        // Near 1: e = 0, y far below the fixed-point resolution of the series.
        cases["LogNearOne/1+2^-150"] = new LogCase((one << 150) + 1, -150, 4);
        cases["LogNearOne/1-2^-150"] = new LogCase((one << 150) - 1, -150, 4);
        cases["LogNearOne/1+2^-1000"] = new LogCase((one << 1000) + 1, -1000, 4);

        // A 64-word significand, much longer than the result.
        var random = new Random(628);
        var longSig = new byte[64 * 4];
        random.NextBytes(longSig);
        longSig[^1] |= 0x80;
        var longValue = new BigInteger(longSig, isUnsigned: true);
        cases["LogLongSignificand/seed628/x~1.x"] = new LogCase(longValue, -(64 * 32 - 1), 4);
        cases["LogLongSignificand/seed628/x~2^20"] = new LogCase(longValue, -(64 * 32 - 1) + 20, 4);

        // Precision ladder for ln 3: word boundaries and more terms than the old 500-term limit (n = 129).
        // n = 5 has the series remainder eps'_R closest to its limit 1 (review of stages 1-4).
        foreach (var words in new[] { 1, 2, 5, 31, 32, 33, 72, 73, 129 })
        {
            cases[$"LogPrecisionLadder/3/n={words}"] = new LogCase(3, 0, words);
        }

        // Series forms: Horner always (int.MaxValue), at most 8 terms (default) and never (0), with the
        // Paterson–Stockmeyer block size 2, 3, the planner's choice and ceil(sqrt N) + 3. x = 3 (e = 2, c = 1, many
        // terms), 1 + 2^-10 (e = 0, c = 9, about 8 terms) and 1 + 2^-150 (e = 0, c = 149, one term).
        foreach (var (significand, exponent, e, closeness, label) in new[]
                 {
                     (new BigInteger(3), 0, 2L, 1, "3"),
                     ((one << 10) + 1, -10, 0L, 9, "1+2^-10"),
                     ((one << 150) + 1, -150, 0L, 149, "1+2^-150"),
                 })
        {
            foreach (var words in new[] { 4, 32 })
            {
                var terms = TranscendentalPlanner.PlanLog(32 * words, e, closeness, new GpuBigFloatTranscendentalOptions()).Terms;
                foreach (var hornerMaxTerms in new[] { int.MaxValue, 8, 0 })
                {
                    foreach (var blockSize in new int?[] { 2, 3, null, (int)Math.Ceiling(Math.Sqrt(terms)) + 3 })
                    {
                        if (hornerMaxTerms == int.MaxValue && blockSize is not null)
                        {
                            continue; // Horner ignores the block size.
                        }

                        cases[$"LogSeriesForms/{label}/n={words}/horner<={hornerMaxTerms}/m={blockSize?.ToString() ?? "auto"}"] =
                            new LogCase(significand, exponent, words, blockSize, hornerMaxTerms);
                    }
                }
            }
        }

        return cases;
    }
}
