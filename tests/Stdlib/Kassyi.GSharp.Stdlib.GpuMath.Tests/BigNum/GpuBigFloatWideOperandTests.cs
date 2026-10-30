#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Wide operands of the GPU <c>Divide</c> and <c>Rsqrt</c> (#661 §4.12): within the measured widths
/// (<see cref="WideOperandOptions"/>), a divisor or radicand of at least <c>n + 2 + g</c> words is cut to its top
/// <c>n + 2</c> words, and one full-width multiplication corrects the estimate when its discarded bits leave the result
/// open.
/// </summary>
/// <remarks>
/// <para>
/// The results must be the same bits as before: <c>RZ_p</c> of the exact value (<see cref="BigFloatReference"/>), the
/// same as the CPU provider and as the full-width path. Which path ran is read from the counters of the workspace and
/// predicted here from the operands: truncation from the widths, the check from the estimate computed with
/// <see cref="BigInteger"/> from the top words (the low word of the inverse root, the low 2 bits of the quotient).
/// </para>
/// <para>
/// The cases that need the correction are built around a chosen value: <c>2^(2e) + 1</c> (the top words are a power of
/// four, the estimate a power of two one above the floor), <c>2^(b-1)</c> over <c>2^(b-1) + 1</c>, and operands just
/// below or exactly on a chosen inverse root or quotient whose discarded bits are all 0 (where an estimate one too
/// large rounds to a different value).
/// </para>
/// </remarks>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class GpuBigFloatWideOperandTests : IDisposable
{
    // The case tables check the cut on small operands, below the divisor width from which Divide cuts by default
    // (WideOperandOptions.DivideMinDivisorWords); the default widths have their own tests.
    private const int CaseMinDivisorWords = 0;

    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBigFloatWideOperandTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
        _workspace.WideOperands.DivideMinDivisorWords = CaseMinDivisorWords;
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    private sealed record RsqrtCase(uint[] Words, int Exponent, int ResultWords);

    private sealed record DivideCase(uint[] Left, int LeftExponent, bool LeftNegative, uint[] Right, int RightExponent,
        int ResultWords);

    private static readonly Dictionary<string, RsqrtCase> s_rsqrtCases = CreateRsqrtCases();

    private static readonly Dictionary<string, DivideCase> s_divideCases = CreateDivideCases();

    public static TheoryData<string> RsqrtCaseNames() => new(s_rsqrtCases.Keys);

    public static TheoryData<string> DivideCaseNames() => new(s_divideCases.Keys);

    // P1: Rsqrt is RZ_p(1 / sqrt(a)) (BigFloatReference.RzRsqrt) and the same bits as the CPU provider. P2: the operand
    // is truncated exactly when its normalized width w is at least n + 2 + g, and the full-width check runs exactly
    // when the low word of the estimate from the top n + 2 words is 0.
    // mutant: WideOperandOptions.cs:96 `>= ThresholdWords` -> `>` (At*,
    // OddExponentFullWidthOntoThreshold* and the w = n + 6 cases: the path count; WideOperandThreshold_SwitchesThePath)
    // mutant: GSharpKernelFloatProvider.cs:470 `ReadWord(...) == 0` -> never true (the skip condition always true:
    // PowerOfFourPlus1*, HardDecrement* stay one too large)
    // mutant: GSharpKernelFloatProvider.cs:470 `ReadWord(...) == 0` -> always true (Random*, HardSkip*, At*: the check
    // count)
    // mutant: GSharpKernelFloatProvider.cs:465 `Slice(words - truncatedWords` -> `... - 1` (j one word short in
    // the slice: every truncated case)
    // mutant: GSharpKernelFloatProvider.cs:469 `16L * (words - truncatedWords)` -> `16L * (words - truncatedWords - 1)`
    // (j one word short in B: every truncated case)
    // mutant: GSharpKernelFloatProvider.cs:478 `CorrectTruncatedEstimate(...)` removed (PowerOfFourPlus1*,
    // HardDecrement*)
    // mutant: GSharpKernelFloatProvider.cs:463 `NumWords + 2` -> `NumWords + 0` (K = n: Random*, HardSkip*,
    // HardDecrement*)
    // mutant: GSharpKernelFloatProvider.cs:464 `(NumWords + 1) / 2` -> `NumWords / 2` (HardDecrement1, HardDecrement3:
    // for odd n the estimate has p + 17 bits, so a set low word no longer means a set discarded bit)
    // mutant: GpuBigIntDivRemKernels.cu.cs:227 `left > right ? 2ul : 1ul` -> `1ul : 2ul` (the check inverted:
    // PowerOfFourPlus1*, HardDecrement* keep the estimate)
    // mutant: GpuBigIntDivRemKernels.cu.cs:232 `== 2ul` -> `!= 1ul` (product >= bound: PowerOfFour* drop to one below)
    // mutant: GpuBigIntDivRemKernels.cu.cs:232 `== 2ul` -> `!= 0ul` (any difference: HardExact*, where x_hi²·A < 2^(2B),
    // drop to one below)
    // mutant: GpuBigIntDivRemKernels.cu.cs:234 `AddSmallSigned(estimate, estimateWords, -1)` -> `0` (PowerOfFourPlus1*,
    // HardDecrement*)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(RsqrtCaseNames))]
    public void Rsqrt_WideRadicand_IsRoundedTowardZero(string caseName)
    {
        var rsqrtCase = s_rsqrtCases[caseName];
        var p = 32 * rsqrtCase.ResultWords;
        var expected = BigFloatReference.RzRsqrt(FromWords(rsqrtCase.Words), rsqrtCase.Exponent, p);
        var path = ExpectedRsqrtPath(rsqrtCase, _workspace.WideOperands);

        var before = Counts();
        var gpu = RunRsqrt(GSharpKernelFloatProvider.Instance, rsqrtCase);
        var ran = Counts() - before;
        var cpu = RunRsqrt(CSharpCpuBigFloatArithmetic.Instance, rsqrtCase);

        Assert.True(expected == gpu, $"{caseName}: expected 0x{expected.Significand:X} * 2^{expected.Exponent}, GPU 0x{gpu.Significand:X} * 2^{gpu.Exponent}");
        Assert.True(expected == cpu, $"{caseName}: expected 0x{expected.Significand:X} * 2^{expected.Exponent}, CPU 0x{cpu.Significand:X} * 2^{cpu.Exponent}");
        Assert.Equal(path, ran);
    }

    // P1: Divide is RZ_p(L / R) (BigFloatReference.RoundTowardZero) and the same bits as the CPU provider. P2: the
    // operand is truncated exactly when R has at least n + 2 + g words, and the full-width check runs exactly when R
    // was cut (j > 0) and the low 2 bits of the estimate are both 0.
    // mutant: WideOperandOptions.cs:88 `>= ThresholdWords` -> `>` (At*, LeadingZeroDivisor* and the
    // w = n + 6 cases: the path count; WideOperandThreshold_SwitchesThePath)
    // mutant: GSharpKernelFloatProvider.cs:444 `truncation > 0` -> `truncation >= 0` (LeadingZeroDivisor*: the check
    // count)
    // mutant: GSharpKernelFloatProvider.cs:444 `& 3) == 0` -> never true (the skip condition always true: PowerPlus1*,
    // HardDecrement* stay one too large)
    // mutant: GSharpKernelFloatProvider.cs:444 `& 3) == 0` -> always true (the check count)
    // mutant: GSharpKernelFloatProvider.cs:427 `rightBits - 32 * truncatedWords` -> `... - 32` (j one word short: the
    // cut divisor has k + 32 bits and DivRem rejects the quotient buffer in WideOperands_LargeWidths and others)
    // equivalent: GSharpKernelFloatProvider.cs:427 `... + 32` (j one word longer) survives: the cut divisor keeps k -
    // 32 bits, the excess stays below 2^(p + 37 - k) = 2^-27, and the estimate is still the floor or one above; only
    // the cost changes
    // mutant: GSharpKernelFloatProvider.cs:428 `rightBits + 32 * n + 2` -> `rightBits + 32 * n` (HardDecrement1, 4, 8:
    // the estimate keeps fewer than 2 discarded bits, so its low bits no longer decide; and the check count)
    // mutant: GSharpKernelFloatProvider.cs:448 `CorrectTruncatedEstimate(...)` removed (PowerPlus1*, HardDecrement*)
    // mutant: GSharpKernelFloatProvider.cs:425 `n + 2` -> `n` (K = n: the excess reaches 2^5; NarrowDividend*,
    // WiderDividend*, Hard*)
    // mutant: GpuBigIntDivRemKernels.cu.cs:227 `left > right ? 2ul : 1ul` -> `1ul : 2ul` (PowerPlus1*, HardDecrement*)
    // mutant: GpuBigIntDivRemKernels.cu.cs:232 `== 2ul` -> `!= 1ul` (SameValue*, HardExact*: an exact quotient drops)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(DivideCaseNames))]
    public void Divide_WideDivisor_IsRoundedTowardZero(string caseName)
    {
        var divideCase = s_divideCases[caseName];
        var p = 32 * divideCase.ResultWords;
        var expected = ExpectedQuotient(divideCase);
        var path = ExpectedDividePath(divideCase, _workspace.WideOperands);

        var before = Counts();
        var gpu = RunDivide(GSharpKernelFloatProvider.Instance, divideCase);
        var ran = Counts() - before;
        var cpu = RunDivide(CSharpCpuBigFloatArithmetic.Instance, divideCase);

        Assert.True(expected == gpu, $"{caseName}: expected {Describe(expected)}, GPU {Describe(gpu)}");
        Assert.True(expected == cpu, $"{caseName}: expected {Describe(expected)}, CPU {Describe(cpu)}");
        Assert.True(gpu.Significand.IsZero || gpu.Significand.GetBitLength() == p, $"{caseName}: {gpu.Significand.GetBitLength()} bits");
        Assert.Equal(path, ran);
    }

    // Coverage witnesses of the case tables (default threshold): the full-width path, the truncated path with the check
    // skipped (the estimate exact, and one above), and with the check run (the estimate one above and decremented, and
    // exact and kept).
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CaseTables_ReachEveryBranch()
    {
        var defaults = new WideOperandOptions { DivideMinDivisorWords = CaseMinDivisorWords };
        var rsqrt = s_rsqrtCases.Values.Select(c => RsqrtBranch(c, defaults)).ToList();
        var divide = s_divideCases.Values.Select(c => DivideBranch(c, defaults)).ToList();
        foreach (var branches in new[] { rsqrt, divide })
        {
            Assert.Contains(new Branch(false, false, false), branches);
            Assert.Contains(new Branch(true, false, false), branches);
            Assert.Contains(new Branch(true, false, true), branches);
            Assert.Contains(new Branch(true, true, true), branches);
            Assert.Contains(new Branch(true, true, false), branches);
        }
    }

    // A zero dividend over a wide divisor is 0 (the result words cleared, the sign not negative) without cutting the
    // divisor; the bit length read of the dividend decides it before any other work.
    // mutant: GSharpKernelFloatProvider.cs:419 ClearMemoryAsync removed (the result keeps its old words)
    // mutant: GSharpKernelFloatProvider.cs:420 `new GpuFloatResult(0, false)` -> `new GpuFloatResult(0, true)`
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(4)]
    public void Divide_ZeroByWideDivisor_IsZero(int n)
    {
        using var left = new GpuBigUInt(_workspace, new uint[3]);
        var divisorWords = n + 2 + WideOperandOptions.DefaultThresholdWords;
        using var right = new GpuBigUInt(_workspace, RandomWords(new Random(4190 + n), divisorWords));
        using var result = new GpuBigUInt(_workspace, Enumerable.Repeat(uint.MaxValue, n).ToArray());
        var context = CreateContext();
        var leftOperand = new GpuFloatOperand(left.Handle, 7, true);
        var rightOperand = new GpuFloatOperand(right.Handle, -3);

        var before = Counts();
        var quotient = GSharpKernelFloatProvider.Instance.Divide(ref context, in leftOperand, in rightOperand, result.Handle);

        Assert.Equal((BigInteger.Zero, 0, false), (result.ToBigInteger(), quotient.Exponent, quotient.IsNegative));
        Assert.Equal(new PathCounts(0, 0), Counts() - before);
    }

    // The switch follows the setting: with g words of threshold, an operand of n + 1 + g words takes the full-width
    // path and one of n + 2 + g words the truncated one (g = 0 truncates nothing away: the top n + 2 words are the
    // operand). Both give RZ_p. The radicands fill their words and have even exponents, so their width is that of the
    // significand.
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void WideOperandThreshold_SwitchesThePath(int threshold)
    {
        _workspace.WideOperands.ThresholdWords = threshold;
        var random = new Random(4140 + threshold);
        foreach (var n in new[] { 1, 3 })
        {
            foreach (var words in new[] { n + 1 + threshold, n + 2 + threshold })
            {
                var rsqrtCase = new RsqrtCase(RandomWords(random, words), 2 * random.Next(-25, 25), n);
                var expected = BigFloatReference.RzRsqrt(FromWords(rsqrtCase.Words), rsqrtCase.Exponent, 32 * n);
                var before = Counts();
                var gpu = RunRsqrt(GSharpKernelFloatProvider.Instance, rsqrtCase);
                var ran = Counts() - before;
                Assert.True(expected == gpu, $"Rsqrt g={threshold}, n={n}, w={words}");
                Assert.Equal(words >= n + 2 + threshold ? 1 : 0, ran.Truncations);

                var divideCase = new DivideCase(RandomWords(random, words), 3, false, RandomWords(random, words), -5, n);
                before = Counts();
                var quotient = RunDivide(GSharpKernelFloatProvider.Instance, divideCase);
                ran = Counts() - before;
                Assert.True(ExpectedQuotient(divideCase) == quotient, $"Divide g={threshold}, n={n}, w={words}");
                Assert.Equal(words >= n + 2 + threshold ? 1 : 0, ran.Truncations);
            }
        }
    }

    // A workspace starts with the measured defaults, and a negative threshold is rejected (the setting keeps its
    // value).
    // mutant: WideOperandOptions.cs:66 `ThrowIfNegative(value)` removed
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void WideOperandOptions_DefaultsAndNegativeThreshold()
    {
        using var workspace = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);
        var options = workspace.WideOperands;
        Assert.Equal(
            (WideOperandOptions.DefaultThresholdWords, WideOperandOptions.DefaultDivideMaxResultWords,
                WideOperandOptions.DefaultDivideMinDivisorWords, WideOperandOptions.DefaultRsqrtMaxResultWords),
            (options.ThresholdWords, options.DivideMaxResultWords, options.DivideMinDivisorWords,
                options.RsqrtMaxResultWords));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.ThresholdWords = -1);
        Assert.Equal(WideOperandOptions.DefaultThresholdWords, options.ThresholdWords);
        options.ThresholdWords = int.MaxValue;
        Assert.Equal(int.MaxValue, options.ThresholdWords);
    }

    // The cut stays within the measured widths: with the default options, Divide cuts the divisor for
    // n = DivideMaxResultWords and not for one word more, and a divisor of DivideMinDivisorWords words and not one word
    // narrower; Rsqrt cuts the radicand for n = RsqrtMaxResultWords and not for one more. The operands have n + 2 + g
    // words (the narrowest that is cut) or, for the divisor width, n = 4; every result is RZ_p either way.
    // mutant: WideOperandOptions.cs:86 `resultWords <= DivideMaxResultWords` -> `<` (the Divide at the limit is not
    // cut)
    // mutant: WideOperandOptions.cs:86 `resultWords <= DivideMaxResultWords` removed (the Divide above the limit is
    // cut)
    // mutant: WideOperandOptions.cs:87 `divisorWords >= DivideMinDivisorWords` -> `>` (the divisor at the limit is not
    // cut)
    // mutant: WideOperandOptions.cs:87 `divisorWords >= DivideMinDivisorWords` removed (the narrower divisor is cut)
    // mutant: WideOperandOptions.cs:96 `resultWords <= RsqrtMaxResultWords` -> `<` (the Rsqrt at the limit is not cut)
    // mutant: WideOperandOptions.cs:96 `resultWords <= RsqrtMaxResultWords` removed (the Rsqrt above the limit is cut)
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void WideOperandLimits_SwitchThePath()
    {
        var options = _workspace.WideOperands;
        options.DivideMinDivisorWords = WideOperandOptions.DefaultDivideMinDivisorWords;
        var random = new Random(4146);
        foreach (var n in new[] { options.DivideMaxResultWords, options.DivideMaxResultWords + 1 })
        {
            var divideCase = new DivideCase(RandomWords(random, n), 3, false,
                RandomWords(random, n + 2 + options.ThresholdWords), -5, n);
            var before = Counts();
            var quotient = RunDivide(GSharpKernelFloatProvider.Instance, divideCase);
            var ran = Counts() - before;
            Assert.True(ExpectedQuotient(divideCase) == quotient, $"Divide n={n}");
            Assert.Equal((n, n <= WideOperandOptions.DefaultDivideMaxResultWords ? 1 : 0), (n, ran.Truncations));
        }

        foreach (var words in new[] { options.DivideMinDivisorWords - 1, options.DivideMinDivisorWords })
        {
            var divideCase = new DivideCase(RandomWords(random, 4), 3, false, RandomWords(random, words), -5, 4);
            var before = Counts();
            var quotient = RunDivide(GSharpKernelFloatProvider.Instance, divideCase);
            var ran = Counts() - before;
            Assert.True(ExpectedQuotient(divideCase) == quotient, $"Divide w={words}");
            Assert.Equal(
                (words, words >= WideOperandOptions.DefaultDivideMinDivisorWords ? 1 : 0),
                (words, ran.Truncations));
        }

        foreach (var n in new[] { options.RsqrtMaxResultWords, options.RsqrtMaxResultWords + 1 })
        {
            var radicand = RandomWords(random, n + 2 + options.ThresholdWords);
            var rsqrtCase = new RsqrtCase(radicand, 2 * random.Next(-25, 25), n);
            var expected = BigFloatReference.RzRsqrt(FromWords(rsqrtCase.Words), rsqrtCase.Exponent, 32 * n);
            var before = Counts();
            var rsqrt = RunRsqrt(GSharpKernelFloatProvider.Instance, rsqrtCase);
            var ran = Counts() - before;
            Assert.True(expected == rsqrt, $"Rsqrt n={n}");
            Assert.Equal((n, n <= WideOperandOptions.DefaultRsqrtMaxResultWords ? 1 : 0), (n, ran.Truncations));
        }
    }

    // The truncated path returns the same bits as the full-width path (g = int.MaxValue), and both are RZ_p, over
    // seeded random operands of 1 to 6 result words and n + 2 to 16n + 2 operand words with random exponents and signs.
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(40)]
    public void WideOperands_SeededSweep_MatchTheFullWidthPath(int casesPerOperation)
    {
        var random = new Random(4150);
        for (var i = 0; i < casesPerOperation; i++)
        {
            var n = random.Next(1, 7);
            var words = random.Next(n + 2, 16 * n + 3);
            var rsqrtCase = new RsqrtCase(RandomWords(random, words), random.Next(-1000, 1000), n);
            var divideCase = new DivideCase(RandomWords(random, random.Next(1, 16 * n + 3)), random.Next(-1000, 1000),
                random.Next(2) == 0, RandomWords(random, words), random.Next(-1000, 1000), n);

            _workspace.WideOperands.ThresholdWords = 0;
            var truncatedRsqrt = RunRsqrt(GSharpKernelFloatProvider.Instance, rsqrtCase);
            var truncatedQuotient = RunDivide(GSharpKernelFloatProvider.Instance, divideCase);
            _workspace.WideOperands.ThresholdWords = int.MaxValue;
            var fullRsqrt = RunRsqrt(GSharpKernelFloatProvider.Instance, rsqrtCase);
            var fullQuotient = RunDivide(GSharpKernelFloatProvider.Instance, divideCase);

            var expectedRsqrt = BigFloatReference.RzRsqrt(FromWords(rsqrtCase.Words), rsqrtCase.Exponent, 32 * n);
            Assert.True(expectedRsqrt == truncatedRsqrt && expectedRsqrt == fullRsqrt, $"Rsqrt case {i}: n={n}, w={words}");
            var expectedQuotient = ExpectedQuotient(divideCase);
            Assert.True(
                expectedQuotient == truncatedQuotient && expectedQuotient == fullQuotient,
                $"Divide case {i}: n={n}, w={words}: expected {Describe(expectedQuotient)}, truncated {Describe(truncatedQuotient)}, full {Describe(fullQuotient)}");
        }
    }

    // Large results and operands (n = 16 and 64, operands of 4n and 16n words), including the cases that need the
    // correction. The result-width limits are lifted, so the cut path is checked at n = 64 too.
    [SkippableTheory]
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(16)]
    [InlineData(64)]
    public void WideOperands_LargeWidths_AreRoundedTowardZero(int n)
    {
        _workspace.WideOperands.DivideMaxResultWords = int.MaxValue;
        _workspace.WideOperands.RsqrtMaxResultWords = int.MaxValue;
        var random = new Random(4160 + n);
        foreach (var words in new[] { 4 * n, 16 * n })
        {
            var radicands = new[]
            {
                new RsqrtCase(RandomWords(random, words), 1, n),
                new RsqrtCase(PowerOfTwoPlus(32 * words - 2, 1, words), 0, n),
            };
            foreach (var rsqrtCase in radicands)
            {
                var expected = BigFloatReference.RzRsqrt(FromWords(rsqrtCase.Words), rsqrtCase.Exponent, 32 * n);
                var before = Counts();
                Assert.True(expected == RunRsqrt(GSharpKernelFloatProvider.Instance, rsqrtCase), $"Rsqrt n={n}, w={words}");
                Assert.Equal(ExpectedRsqrtPath(rsqrtCase, _workspace.WideOperands), Counts() - before);
            }

            var divisors = new[]
            {
                new DivideCase(RandomWords(random, 2 * words), 0, false, RandomWords(random, words), 0, n),
                new DivideCase(PowerOfTwoPlus(32 * words - 1, 0, words), 0, false, PowerOfTwoPlus(32 * words - 1, 1, words), 0, n),
            };
            foreach (var divideCase in divisors)
            {
                var before = Counts();
                Assert.True(ExpectedQuotient(divideCase) == RunDivide(GSharpKernelFloatProvider.Instance, divideCase), $"Divide n={n}, w={words}");
                Assert.Equal(ExpectedDividePath(divideCase, _workspace.WideOperands), Counts() - before);
            }
        }
    }

    private static Dictionary<string, RsqrtCase> CreateRsqrtCases()
    {
        var cases = new Dictionary<string, RsqrtCase>();
        var random = new Random(4170);
        var threshold = WideOperandOptions.DefaultThresholdWords;
        foreach (var n in new[] { 1, 2, 3, 4, 8 })
        {
            var at = n + 2 + threshold;

            // At the threshold and one word below it.
            cases[$"Below{n}"] = new(RandomWords(random, at - 1), 0, n);
            cases[$"At{n}"] = new(RandomWords(random, at), 0, n);

            // A significand that fills its words with an odd exponent moves one word up, onto the threshold.
            cases[$"OddExponentFullWidthOntoThreshold{n}"] = new(RandomWords(random, at - 1), 1, n);

            // 2^(2e) + 1 and 2^(2e) (the estimate is a power of two: one too large, and exact); 2^(2e+1) + 1.
            foreach (var words in new[] { at, 4 * n + 2, 16 * n })
            {
                cases[$"PowerOfFourPlus1n{n}w{words}"] = new(PowerOfTwoPlus(32 * words - 2, 1, words), 0, n);
                cases[$"PowerOfFourn{n}w{words}"] = new(PowerOfTwoPlus(32 * words - 2, 0, words), 2, n);
                cases[$"PowerOfFourTimes2Plus1n{n}w{words}"] = new(PowerOfTwoPlus(32 * words - 1, 1, words), -4, n);
                cases[$"Randomn{n}w{words}"] = new(RandomWords(random, words), -7, n);
            }

            // Around a chosen inverse root x with every discarded bit 0: just below it (the estimate x is one too large
            // and rounds up without the correction), just above it (the estimate x is exact and x²·A < 2^(2B): the
            // check runs and must not move it), and the low word not 0 (one too large, skipped).
            var hardWords = at + 1;
            cases[$"HardDecrement{n}"] =
                new(HardRadicand(random, n, hardWords, zeroDiscarded: true, justBelow: true), 0, n);
            cases[$"HardExact{n}"] =
                new(HardRadicand(random, n, hardWords, zeroDiscarded: true, justBelow: false), 0, n);
            cases[$"HardSkip{n}"] =
                new(HardRadicand(random, n, hardWords, zeroDiscarded: false, justBelow: true), 0, n);
        }

        return cases;
    }

    private static Dictionary<string, DivideCase> CreateDivideCases()
    {
        var cases = new Dictionary<string, DivideCase>();
        var random = new Random(4180);
        var threshold = WideOperandOptions.DefaultThresholdWords;
        foreach (var n in new[] { 1, 2, 3, 4, 8 })
        {
            var at = n + 2 + threshold;
            cases[$"Below{n}"] = new(RandomWords(random, at), 0, false, RandomWords(random, at - 1), 0, n);
            cases[$"At{n}"] = new(RandomWords(random, at), 0, false, RandomWords(random, at), 0, n);

            foreach (var words in new[] { at, 4 * n + 2, 16 * n })
            {
                var power = PowerOfTwoPlus(32 * words - 1, 0, words);
                var powerPlus1 = PowerOfTwoPlus(32 * words - 1, 1, words);
                cases[$"PowerPlus1n{n}w{words}"] = new(power, 0, false, powerPlus1, 0, n);
                var same = RandomWords(random, words);
                cases[$"SameValuen{n}w{words}"] = new(same, 5, true, same, 5, n);
                cases[$"NarrowDividendn{n}w{words}"] = new(RandomWords(random, 1), -100, false, RandomWords(random, words), 37, n);
                cases[$"WiderDividendn{n}w{words}"] = new(RandomWords(random, 2 * words + 1), 0, true, RandomWords(random, words), 0, n);
            }

            // Around a chosen quotient q of p + 2 or p + 3 bits with every discarded bit 0: N = q·R - 1 (the estimate q
            // is one too large), N = q·R (exact: the check must not move it); and q with its low bit set (one too
            // large, skipped).
            var (decrement, exact, skip) = HardDivideOperands(random, n, at + 1);
            cases[$"HardDecrement{n}"] = decrement;
            cases[$"HardExact{n}"] = exact;
            cases[$"HardSkip{n}"] = skip;

            // A divisor in at least n + 2 + g words whose value has fewer than k bits: truncated path, nothing cut, so
            // the estimate is exact and no check runs even when its low 2 bits are 0.
            cases[$"LeadingZeroDivisor{n}"] = LeadingZeroDivisor(random, n, at);
        }

        return cases;
    }

    private static PathCounts ExpectedRsqrtPath(RsqrtCase rsqrtCase, WideOperandOptions options) =>
        RsqrtBranch(rsqrtCase, options).Counts;

    private static PathCounts ExpectedDividePath(DivideCase divideCase, WideOperandOptions options) =>
        DivideBranch(divideCase, options).Counts;

    // The branch of an Rsqrt case: §4.4 step 2 gives A and its width w; truncated when w >= n + 2 + g and
    // n <= RsqrtMaxResultWords; the check runs when the low word of x_hi = floor(2^B_t / sqrt(floor(A / 2^j))) is 0;
    // one above when x_hi is floor(2^B / sqrt(A)) + 1.
    private static Branch RsqrtBranch(RsqrtCase rsqrtCase, WideOperandOptions options)
    {
        var (radicand, words) = NormalizedRadicand(rsqrtCase.Words, rsqrtCase.Exponent);
        var truncatedWords = rsqrtCase.ResultWords + 2;
        if (words - truncatedWords < options.ThresholdWords || rsqrtCase.ResultWords > options.RsqrtMaxResultWords)
        {
            return default;
        }

        var j = 32 * (words - truncatedWords);
        var bt = 32 * (truncatedWords + (rsqrtCase.ResultWords + 1) / 2);
        var estimate = BigFloatReference.IntegerSqrt((BigInteger.One << (2 * bt)) / (radicand >> j));
        var exact = BigFloatReference.IntegerSqrt((BigInteger.One << (2 * bt + j)) / radicand);
        return new Branch(true, (estimate & uint.MaxValue) == 0, estimate != exact);
    }

    // The branch of a Divide case: truncated when R has at least n + 2 + g and at least DivideMinDivisorWords words and
    // n <= DivideMaxResultWords; the check runs when j = bits(R) - k > 0 and the low 2 bits of
    // Q_hi = floor(floor(N / 2^j) / floor(R / 2^j)) are 0; one above when Q_hi = floor(N / R) + 1.
    private static Branch DivideBranch(DivideCase divideCase, WideOperandOptions options)
    {
        var n = divideCase.ResultWords;
        if (divideCase.Right.Length - (n + 2) < options.ThresholdWords || n > options.DivideMaxResultWords
            || divideCase.Right.Length < options.DivideMinDivisorWords)
        {
            return default;
        }

        var left = FromWords(divideCase.Left);
        var right = FromWords(divideCase.Right);
        var j = (int)right.GetBitLength() - 32 * (n + 2);
        if (j <= 0)
        {
            return new Branch(true, false, false);
        }

        var shift = (int)right.GetBitLength() + 32 * n + 2 - (int)left.GetBitLength();
        var scaled = shift >= 0 ? left << shift : left >> -shift;
        var estimate = (scaled >> j) / (right >> j);
        return new Branch(true, (estimate & 3) == 0, estimate != scaled / right);
    }

    // §4.4 step 2: A = S·2^t of w words with bit length 32w or 32w - 1 and E - t even; when S fills its words and E is
    // odd, A moves one word up (w + 1 words, t = 31) instead of losing its lowest bit.
    private static (BigInteger Radicand, int Words) NormalizedRadicand(uint[] words, int exponent)
    {
        var significand = FromWords(words);
        var width = words.Length;
        var t = 32L * width - (long)significand.GetBitLength();
        if (((exponent - t) & 1) != 0)
        {
            if (t > 0)
            {
                t--;
            }
            else
            {
                width++;
                t = 31;
            }
        }

        return (significand << (int)t, width);
    }

    // RZ_p of |L·2^eL / (R·2^eR)| as num / den; RunDivide checks the sign.
    private static (BigInteger Significand, long Exponent) ExpectedQuotient(DivideCase divideCase)
    {
        var num = FromWords(divideCase.Left) << Math.Max(0, divideCase.LeftExponent - divideCase.RightExponent);
        var den = FromWords(divideCase.Right) << Math.Max(0, divideCase.RightExponent - divideCase.LeftExponent);
        return BigFloatReference.RoundTowardZero(num, den, 32 * divideCase.ResultWords);
    }

    // A radicand of w words (E = 0, so A is the significand) around the inverse root x: x has every discarded bit 0 (or
    // its low bit set). Just below x, A = ceil(2^(2B) / x²) with A_lo != 0 makes the truncated estimate x and the floor
    // x - 1; just above x, A = floor(2^(2B) / x²) makes both x, with x²·A < 2^(2B).
    private static uint[] HardRadicand(Random random, int n, int words, bool zeroDiscarded, bool justBelow)
    {
        var truncatedWords = n + 2;
        var b = 32 * (truncatedWords + (n + 1) / 2) + 16 * (words - truncatedWords);
        var j = 32 * (words - truncatedWords);
        while (true)
        {
            var x = RandomBits(random, b - 16 * words + 1);
            var discarded = (int)x.GetBitLength() - 32 * n;
            x = zeroDiscarded ? x >> discarded << discarded : x | 1;
            var bound = BigInteger.One << (2 * b);
            var radicand = justBelow ? (bound + x * x - 1) / (x * x) : bound / (x * x);
            var bits = radicand.GetBitLength();
            var lowPart = radicand & ((BigInteger.One << j) - 1);
            if (bits >= 32 * words - 1 && bits <= 32 * words && !lowPart.IsZero && x * x * radicand != bound)
            {
                return ToWords(radicand, words);
            }
        }
    }

    // Operands around a chosen quotient q (p + 2 or p + 3 bits): N = q·R - 1 and N = q·R with every discarded bit of
    // q 0, and N = q·R - 1 with the low bit of q set; N has bits(R) + p + 2 bits, so it is the dividend itself (s = 0).
    private static (DivideCase Decrement, DivideCase Exact, DivideCase Skip) HardDivideOperands(Random random, int n, int words)
    {
        var p = 32 * n;
        DivideCase? decrement = null;
        DivideCase? exact = null;
        DivideCase? skip = null;
        while (decrement is null || exact is null || skip is null)
        {
            var divisor = RandomBits(random, 32 * words - random.Next(31));
            if ((divisor & ((BigInteger.One << ((int)divisor.GetBitLength() - 32 * (n + 2))) - 1)).IsZero)
            {
                continue;
            }

            var quotient = RandomBits(random, p + 2 + random.Next(2));
            var discarded = (int)quotient.GetBitLength() - p;
            var cleared = quotient >> discarded << discarded;
            var odd = quotient | 1;
            var targetBits = divisor.GetBitLength() + p + 2;
            if ((cleared * divisor - 1).GetBitLength() == targetBits && (cleared * divisor).GetBitLength() == targetBits)
            {
                decrement ??= new(ToWords(cleared * divisor - 1), 0, false, ToWords(divisor, words), 0, n);
                exact ??= new(ToWords(cleared * divisor), 0, false, ToWords(divisor, words), 0, n);
            }

            if ((odd * divisor - 1).GetBitLength() == targetBits)
            {
                skip ??= new(ToWords(odd * divisor - 1), 0, false, ToWords(divisor, words), 0, n);
            }
        }

        return (decrement, exact, skip);
    }

    // A one-word divisor stored in `words` words, with a dividend whose quotient estimate has its low 2 bits 0.
    private static DivideCase LeadingZeroDivisor(Random random, int n, int words)
    {
        var divisorWords = new uint[words];
        divisorWords[0] = (uint)random.Next(3, int.MaxValue) | 1u;
        var divisor = new BigInteger(divisorWords[0]);
        while (true)
        {
            var dividend = RandomBits(random, 1 + random.Next(64));
            var shift = (int)divisor.GetBitLength() + 32 * n + 2 - (int)dividend.GetBitLength();
            if ((((dividend << shift) / divisor) & 3) == 0)
            {
                return new DivideCase(ToWords(dividend), 0, false, divisorWords, 0, n);
            }
        }
    }

    private readonly record struct Branch(bool Truncated, bool Checked, bool OneAbove)
    {
        public PathCounts Counts => new(Truncated ? 1 : 0, Checked ? 1 : 0);
    }

    private readonly record struct PathCounts(int Truncations, int BoundChecks)
    {
        public static PathCounts operator -(PathCounts after, PathCounts before) =>
            new(after.Truncations - before.Truncations, after.BoundChecks - before.BoundChecks);
    }

    private PathCounts Counts() =>
        new(_workspace.WideOperandCounters.Truncations, _workspace.WideOperandCounters.BoundChecks);

    private (BigInteger Significand, long Exponent) RunRsqrt(IGpuBigFloatArithmetic arithmetic, RsqrtCase rsqrtCase)
    {
        using var value = new GpuBigUInt(_workspace, rsqrtCase.Words);
        using var result = new GpuBigUInt(_workspace, rsqrtCase.ResultWords);
        var context = CreateContext();
        var operand = new GpuFloatOperand(value.Handle, rsqrtCase.Exponent);
        var rsqrt = arithmetic.Rsqrt(ref context, in operand, result.Handle);
        Assert.False(rsqrt.IsNegative);
        return (result.ToBigInteger(), rsqrt.Exponent);
    }

    // The sign is checked here (the dividend's sign; divisors are positive) and the magnitude returned.
    private (BigInteger Significand, long Exponent) RunDivide(IGpuBigFloatArithmetic arithmetic, DivideCase divideCase)
    {
        using var left = new GpuBigUInt(_workspace, divideCase.Left);
        using var right = new GpuBigUInt(_workspace, divideCase.Right);
        using var result = new GpuBigUInt(_workspace, divideCase.ResultWords);
        var context = CreateContext();
        var leftOperand = new GpuFloatOperand(left.Handle, divideCase.LeftExponent, divideCase.LeftNegative);
        var rightOperand = new GpuFloatOperand(right.Handle, divideCase.RightExponent);
        var quotient = arithmetic.Divide(ref context, in leftOperand, in rightOperand, result.Handle);
        var significand = result.ToBigInteger();
        Assert.Equal(divideCase.LeftNegative && !significand.IsZero, quotient.IsNegative);
        return (significand, quotient.Exponent);
    }

    private GpuBigIntExecutionContext CreateContext() =>
        new(_workspace, _workspace.MemoryManager, new LegacyProviderArithmeticAdapter(), _workspace.Stream);

    private static string Describe((BigInteger Significand, long Exponent) value) =>
        $"0x{value.Significand:X} * 2^{value.Exponent}";

    // 2^bit + addend in `words` words.
    private static uint[] PowerOfTwoPlus(int bit, int addend, int words) =>
        ToWords((BigInteger.One << bit) + addend, words);

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

    // A random value of exactly the given bit length.
    private static BigInteger RandomBits(Random random, int bits)
    {
        var bytes = new byte[(bits + 7) / 8 + 1];
        random.NextBytes(bytes);
        bytes[^1] = 0;
        var value = new BigInteger(bytes) & ((BigInteger.One << bits) - 1);
        return value | (BigInteger.One << (bits - 1));
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
}
