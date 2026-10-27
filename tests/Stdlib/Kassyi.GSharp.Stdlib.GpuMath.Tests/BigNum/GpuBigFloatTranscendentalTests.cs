#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Accuracy tests of the big-float <c>Log</c> (and later <c>Exp</c> / <c>Pow</c>) of the GPU provider
/// (<see cref="GSharpKernelFloatProvider"/>) and the CPU provider (<see cref="CSharpCpuBigFloatArithmetic"/>).
/// </summary>
/// <remarks>
/// <para>
/// Every expected value comes from <see cref="BigFloatIntervalOracle"/> and the input that the test holds
/// (significand and exponent), never from a value read back from the GPU. The result has <c>p = 32·n</c> bits for
/// <c>n</c> result words.
/// </para>
/// <para>
/// Contract checked here (stage 2 of #628): the result is a faithful rounding, <c>RZ_p(v)</c> or the next
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
