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
/// Contract checked here (stages 2 and 3 of #628): the result is a faithful rounding, <c>RZ_p(v)</c> or the next
/// <c>p</c>-bit value away from zero, with the significand normalized to the top bit of the result words.
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

    /// <summary>Named Log inputs <c>x = Significand · 2^Exponent</c> with the number of result words.</summary>
    private static readonly Dictionary<string, (BigInteger Significand, int Exponent, int Words)> s_logCases = CreateLogCases();

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

    // P1: the result is RZ_p(ln x) or the next p-bit value away from zero (oracle: e^R <= x < e^(R + ulp)).
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
    public void Log_IsFaithful(string engine, string caseName)
    {
        var (sig, exp, words) = s_logCases[caseName];

        var (rSig, result) = RunLog(Arithmetic(engine), sig, exp, words);

        Assert.True(rSig.IsZero || rSig.GetBitLength() == 32 * words, $"{caseName}: significand not normalized");
        Assert.True(
            BigFloatIntervalOracle.IsFaithfulLog(sig, exp, rSig, result.Exponent, result.IsNegative, 32 * words),
            $"{caseName} [{engine}]: ln x is not within 1 ulp of {Describe(rSig, result)}");
    }

    // The GPU and the CPU provider run the same planner and the same integer steps, so they agree bit for bit.
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(LogCaseNames))]
    public void Log_CpuAndGpuAgree(string caseName)
    {
        var (sig, exp, words) = s_logCases[caseName];

        var gpu = RunLog(GSharpKernelFloatProvider.Instance, sig, exp, words);
        var cpu = RunLog(CSharpCpuBigFloatArithmetic.Instance, sig, exp, words);

        Assert.Equal(cpu, gpu);
    }

    // ln 1 = 0 exactly: a cleared result with exponent 0.
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("Gpu")]
    [InlineData("Cpu")]
    public void Log_OfOne_IsZero(string engine)
    {
        var (rSig, result) = RunLog(Arithmetic(engine), BigInteger.One << 40, -40, 4);

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

    public static TheoryData<string> ExpCaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in s_expCases.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    // P1: the result is RZ_p(e^x) or the next p-bit value away from zero (oracle: interval of e^x).
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ExpCasesOnBothEngines))]
    // mutant: TranscendentalPlanner.cs PlanExp `Math.Floor(xApprox / Ln2) - 1` -> `+ 1` (r < 0): every case
    // mutant: TranscendentalPlanner.cs PlanExp guard loop `if (needed <= guardWords)` -> `if (true)` (gw stays 1 while
    // the bound has up to 2^200·N): ExpForcedHalving s = 40, 200 and ExpPrecisionLadder n = 129
    // mutant: GpuBigFloatTranscendentals.cs / BigFloatTranscendentalMath.cs Exp tiny shortcut `-(resultBits + 2)` ->
    // `-(resultBits - 2)`: ExpTiny ±3·2^-128
    // mutant: GpuBigFloatTranscendentals.cs / BigFloatTranscendentalMath.cs Exp one squaring dropped
    // (`i < plan.Halvings` -> `i < plan.Halvings - 1`): every case outside the tiny shortcut
    public void Exp_IsFaithful(string engine, string caseName)
    {
        var expCase = s_expCases[caseName];
        var p = 32 * expCase.Words;

        var (rSig, result) = RunExp(Arithmetic(engine), expCase);

        var (num, denShift) = BigFloatIntervalOracle.ToRatio(expCase.Significand, expCase.Exponent);
        var (rzSig, rzExp) = BigFloatIntervalOracle.ExpRz(expCase.Negative ? -num : num, denShift, p);
        var succ = BigFloatIntervalOracle.SuccessorAwayFromZero(rzSig, rzExp, p);
        var actual = (rSig, (long)result.Exponent);
        Assert.False(result.IsNegative);
        Assert.True(
            actual == (rzSig, rzExp) || actual == (succ.Significand, succ.Exponent),
            $"{caseName} [{engine}]: {Describe(rSig, result)} is not RZ(e^x) = 0x{rzSig:X} * 2^{rzExp} or the next value");
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

    private (BigInteger Significand, GpuFloatResult Result) RunExp(IGpuBigFloatArithmetic arithmetic, ExpCase expCase)
    {
        using var x = new GpuBigUInt(_workspace, ToWords(expCase.Significand));
        using var result = new GpuBigUInt(_workspace, expCase.Words);
        _workspace.Transcendental.ExpHalvingCount = expCase.Halvings;
        var context = CreateContext();

        var r = arithmetic.Exp(ref context, new GpuFloatOperand(x.Handle, expCase.Exponent, expCase.Negative),
            result.Handle);
        return (result.ToBigInteger(), r);
    }

    private (BigInteger Significand, GpuFloatResult Result) RunLog(
        IGpuBigFloatArithmetic arithmetic, BigInteger sig, int exp, int words)
    {
        using var x = new GpuBigUInt(_workspace, ToWords(sig));
        using var result = new GpuBigUInt(_workspace, words);
        var context = CreateContext();

        var r = arithmetic.Log(ref context, new GpuFloatOperand(x.Handle, exp), result.Handle);
        return (result.ToBigInteger(), r);
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
    private readonly record struct ExpCase(BigInteger Significand, int Exponent, bool Negative, int Words, int? Halvings);

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

        // Tiny arguments: 2^-200 takes the shortcut (|x| < 2^-(p+1)); 2^-129, 2^-128, 3·2^-129 and 3·2^-128 do not.
        // e^(3·2^-128) is more than one ulp away from both shortcut values (1 + 1.5 ulp, 1 - 3 ulp below 1), so a
        // shortcut that also took it would not be faithful.
        foreach (var negative in new[] { false, true })
        {
            var sign = negative ? "-" : "+";
            cases[$"ExpTiny/{sign}2^-200"] = new ExpCase(1, -200, negative, 4, null);
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

        // Precision ladder: the planner picks s = 5, 27 and 57 with 1, 2 and 3 guard words.
        foreach (var words in new[] { 1, 31, 129 })
        {
            cases[$"ExpPrecisionLadder/3/n={words}"] = new ExpCase(3, 0, false, words, null);
        }

        // The largest arguments whose result exponent still fits in int (about +-(2^31 + 127)·ln 2).
        cases["ExpRangeEdge/+1488522000"] = new ExpCase(1488522000, 0, false, 4, null);
        cases["ExpRangeEdge/-1488522000"] = new ExpCase(1488522000, 0, true, 4, null);

        return cases;
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

    private static Dictionary<string, (BigInteger, int, int)> CreateLogCases()
    {
        var cases = new Dictionary<string, (BigInteger, int, int)>();
        var one = BigInteger.One;

        // Control: e = 1..6, |y| <= 1/5.
        cases["LogControl/2"] = (2, 0, 4);
        cases["LogControl/3"] = (3, 0, 4);
        cases["LogControl/100"] = (100, 0, 4);

        // Large and small arguments: the e·ln 2 term (#628 B2: the unreduced series saturates at about 8.18).
        cases["LogLargeArgument/1000"] = (1000, 0, 4);
        cases["LogLargeArgument/2^40"] = (1, 40, 4);
        cases["LogLargeArgument/2^100"] = (1, 100, 4);
        cases["LogLargeArgument/2^-40"] = (1, -40, 4);
        cases["LogLargeArgument/2^-100"] = (1, -100, 4);
        cases["LogLargeArgument/3*2^-1000"] = (3, -1000, 4);

        // Powers of two: m = 1, u = 0, ln x = e·ln 2 alone.
        foreach (var words in new[] { 1, 4 })
        {
            cases[$"LogPowerOfTwo/2^1/n={words}"] = (1, 1, words);
            cases[$"LogPowerOfTwo/2^-1/n={words}"] = (1, -1, words);
            cases[$"LogPowerOfTwo/2^31/n={words}"] = (one << 7, 24, words);
            cases[$"LogPowerOfTwo/2^-1000/n={words}"] = (1, -1000, words);
        }

        // The ends of m in [3/4, 3/2): |y| = 1/5 - O(2^-p) just below 3/2, |y| = 1/7 at 3/4.
        foreach (var words in new[] { 4, 32 })
        {
            var p = 32 * words;
            foreach (var k in new[] { 0, 9 })
            {
                cases[$"LogWorstY/(1.5-2^-{p})*2^{k}/n={words}"] = ((3 * (one << (p - 1))) - 1, k - p, words);
                cases[$"LogWorstY/0.75*2^{k}/n={words}"] = (3, k - 2, words);
            }
        }

        // Near 1: e = 0, y far below the fixed-point resolution of the series.
        cases["LogNearOne/1+2^-150"] = ((one << 150) + 1, -150, 4);
        cases["LogNearOne/1-2^-150"] = ((one << 150) - 1, -150, 4);
        cases["LogNearOne/1+2^-1000"] = ((one << 1000) + 1, -1000, 4);

        // A 64-word significand, much longer than the result.
        var random = new Random(628);
        var longSig = new byte[64 * 4];
        random.NextBytes(longSig);
        longSig[^1] |= 0x80;
        var longValue = new BigInteger(longSig, isUnsigned: true);
        cases["LogLongSignificand/seed628/x~1.x"] = (longValue, -(64 * 32 - 1), 4);
        cases["LogLongSignificand/seed628/x~2^20"] = (longValue, -(64 * 32 - 1) + 20, 4);

        // Precision ladder for ln 3: word boundaries and more terms than the old 500-term limit (n = 129).
        foreach (var words in new[] { 1, 2, 31, 32, 33, 72, 73, 129 })
        {
            cases[$"LogPrecisionLadder/3/n={words}"] = (3, 0, words);
        }

        return cases;
    }
}
