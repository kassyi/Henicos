#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

// Properties of the signed expression widths, against a BigInteger oracle at operand widths 1, 2, 33, 64 and 1025
// words and the unequal pairs between them:
// - a + b and a - b equal the oracle for every pair of signs, and the result has Words(max(bits) + 1) words: the carry
//   of the magnitudes is kept (carry chains through every word);
// - a difference of magnitudes takes its sign from the borrow (also when only the top word differs), and zero is +0;
// - * / % Max Min accept operands of different widths, and their results have the words of their bounds;
// - a target narrower than the result is rejected before anything is written; a wider target receives the exact
//   value and zeros above it (leaves and operator nodes);
// - Fit narrows checked: a value that fits is kept with its sign, one that does not is a domain error;
// - an expression whose sign depends on the values is captured into a CUDA graph, as one whose signs agree.

/// <summary>Signed expression widths on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class SignedWidthTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public SignedWidthTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    public static TheoryData<int, int> WidthPairs() => SignedCases.WidthPairs();

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.Boundary)]
    public void AddSubtract_KeepTheCarry_ForEverySign(int aWords, int bWords) =>
        SignedCases.AddSubtract(_workspace, aWords, bWords);

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.Boundary)]
    public void Difference_TakesItsSignFromTheBorrow_AndZeroIsPositive(int aWords, int bWords) =>
        SignedCases.Differences(_workspace, aWords, bWords);

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.HappyPath)]
    public void OtherOperators_AcceptDifferentWidths(int aWords, int bWords) =>
        SignedCases.OtherOperators(_workspace, aWords, bWords);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ReportedSums_FromTheIssue()
    {
        // (+0xFFFFFFFF) + (+1) and (-0xFFFFFFFF) - (+1) in one word gave 0 and -0
        using var x = SignedCases.Value(_workspace, uint.MaxValue, 1);
        using var minusX = SignedCases.Value(_workspace, -(BigInteger)uint.MaxValue, 1);
        using var one = SignedCases.Value(_workspace, 1, 1);

        using var sum = ((GpuBigIntExpr)x + one).Evaluate(_workspace);
        using var difference = ((GpuBigIntExpr)minusX - one).Evaluate(_workspace);

        Assert.Equal(BigInteger.One << 32, SignedCases.Read(sum));
        Assert.Equal(-(BigInteger.One << 32), SignedCases.Read(difference));
        Assert.Equal(2, sum.Value.NumWords);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(34)]
    [Scenario(TestScenario.SadPath)]
    public void Evaluate_NarrowAndWideTargets_RejectsNarrowAndClearsUpperWordsOfWide(int sourceWords)
    {
        var value = -((BigInteger.One << (32 * sourceWords - 1)) + 7);
        using var source = SignedCases.Value(_workspace, value, sourceWords);
        GpuBigIntExpr leaf = source;

        if (sourceWords > 1)
        {
            using var narrow = SignedCases.Filled(_workspace, sourceWords - 1);
            Assert.Throws<ArgumentException>(() => leaf.Evaluate(narrow));
            Assert.All(narrow.ToHostArray(), static w => Assert.Equal(SignedCases.Fill, w));
        }

        using var wide = SignedCases.Filled(_workspace, sourceWords + 2);
        var result = leaf.Evaluate(wide);
        Assert.Equal(value, SignedCases.Read(result));
        Assert.Equal(SignedCases.Limbs(BigInteger.Abs(value), sourceWords + 2), wide.ToHostArray());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Evaluate_OperatorRootWithNarrowAndWideTargets_RejectsNarrowAndClearsUpperWordsOfWide()
    {
        using var a = SignedCases.Value(_workspace, -(BigInteger)uint.MaxValue, 1);
        using var b = SignedCases.Value(_workspace, -(BigInteger)uint.MaxValue, 1);
        var sum = (GpuBigIntExpr)a + b;

        using var narrow = SignedCases.Filled(_workspace, 1);
        Assert.Throws<ArgumentException>(() => sum.Evaluate(narrow));
        Assert.All(narrow.ToHostArray(), static w => Assert.Equal(SignedCases.Fill, w));

        using var wide = SignedCases.Filled(_workspace, 5);
        var result = sum.Evaluate(wide);
        Assert.Equal(-2 * (BigInteger)uint.MaxValue, SignedCases.Read(result));
        Assert.Equal(SignedCases.Limbs(2 * (BigInteger)uint.MaxValue, 5), wide.ToHostArray());
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Fit_KeepsAValueThatFits_AndReportsOneThatDoesNot()
    {
        using var a = SignedCases.Value(_workspace, -(BigInteger)uint.MaxValue, 1);
        using var b = SignedCases.Value(_workspace, 1, 1);

        // -0xFFFFFFFF + 1 fits in one word
        using var fits = ((GpuBigIntExpr)a + b).Fit(1).Evaluate(_workspace);
        Assert.Equal(1, fits.Value.NumWords);
        Assert.Equal(-(BigInteger)uint.MaxValue + 1, SignedCases.Read(fits));
        _workspace.ThrowIfDeviceErrors();

        // -0xFFFFFFFF - 1 does not
        using var tooWide = ((GpuBigIntExpr)a - b).Fit(1).Evaluate(_workspace);
        Assert.Throws<OverflowException>(() => _workspace.ThrowIfDeviceErrors());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((GpuBigIntExpr)a).Fit(0));
    }

    [Theory]
    [MemberData(nameof(NotCases))]
    [Scenario(TestScenario.Boundary)]
    public void Not_IsMinusXMinusOne_OneBitWider(string value, int words, bool negativeZero) =>
        SignedCases.Not(_workspace, BigInteger.Parse(value), words, negativeZero);

    public static TheoryData<string, int, bool> NotCases() => SignedCases.NotCases();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Not_OfEitherSign_IsCaptured()
    {
        using var positive = SignedCases.Value(_workspace, 5, 1);
        using var negative = SignedCases.Value(_workspace, -5, 1);

        using (var graph = (~(GpuBigIntExpr)positive).CompileToGraph(_workspace))
        {
            graph.Execute();
            using var owned = graph.Result.ToOwned();
            Assert.Equal(-6, SignedCases.Read(owned));
        }

        // A negative value's ~ is decided on the device (-0 tells apart there), so it is captured as well
        using (var graph = (~(GpuBigIntExpr)negative).CompileToGraph(_workspace))
        {
            graph.Execute();
            using var owned = graph.Result.ToOwned();
            Assert.Equal(4, SignedCases.Read(owned));
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Capture_OfAValueDependentSign_AndOfSameSigns_Works()
    {
        using var a = SignedCases.Value(_workspace, 5, 2);
        using var b = SignedCases.Value(_workspace, 7, 2);
        using var minusB = SignedCases.Value(_workspace, -7, 2);

        using (var graph = ((GpuBigIntExpr)a + b).CompileToGraph(_workspace))
        {
            graph.Execute();
            using var owned = graph.Result.ToOwned();
            Assert.Equal(12, SignedCases.Read(owned));
        }

        // The sign of a + (-b) is written on the device and read after the launch
        using (var graph = ((GpuBigIntExpr)a + minusB).CompileToGraph(_workspace))
        {
            graph.Execute();
            using var owned = graph.Result.ToOwned();
            Assert.Equal(-2, SignedCases.Read(owned));
        }

        // The workspace is still usable afterwards
        using var after = ((GpuBigIntExpr)a + minusB).Evaluate(_workspace);
        Assert.Equal(-2, SignedCases.Read(after));
    }
}

/// <summary>Signed expression widths on the CPU fallback.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class SignedWidthCpuFallbackTests
{
    public static TheoryData<int, int> WidthPairs() => SignedCases.WidthPairs();

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.Boundary)]
    public void SignedWidth_CpuFallbackWithVaryingWords_FollowsTheSameRules(int aWords, int bWords)
    {
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(1 << 16);
            SignedCases.AddSubtract(workspace, aWords, bWords);
            SignedCases.Differences(workspace, aWords, bWords);
            SignedCases.OtherOperators(workspace, aWords, bWords);
            foreach (var (value, words, negativeZero) in SignedCases.NotValues())
            {
                SignedCases.Not(workspace, BigInteger.Parse(value), words, negativeZero);
            }

            using var a = SignedCases.Value(workspace, -SignedCases.AllOnes(32 * aWords), aWords);
            using var one = SignedCases.Value(workspace, 1, 1);
            Assert.Throws<OverflowException>(() => ((GpuBigIntExpr)a - one).Fit(aWords).Evaluate(workspace));
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }
}

/// <summary>Inputs and oracle checks shared by the GPU and CPU fallback tests.</summary>
internal static class SignedCases
{
    public const uint Fill = 0xDEADBEEFu;

    private static readonly int[] s_widths = [1, 2, 33, 64, 1025];

    public static TheoryData<int, int> WidthPairs()
    {
        var data = new TheoryData<int, int>();
        foreach (var a in s_widths)
        {
            foreach (var b in s_widths)
            {
                data.Add(a, b);
            }
        }

        return data;
    }

    public static BigInteger AllOnes(int bits) => (BigInteger.One << bits) - 1;

    public static GpuBigInt Value(GpuWorkspace workspace, BigInteger value, int words)
    {
        var limbs = new uint[words];
        BigInteger.Abs(value).WriteLimbs(limbs);
        return new GpuBigInt(new GpuBigUInt(workspace, limbs), value.Sign < 0);
    }

    public static GpuBigUInt Filled(GpuWorkspace workspace, int words) =>
        new(workspace, Enumerable.Repeat(Fill, words).ToArray());

    public static BigInteger Read(GpuBigInt value)
    {
        var magnitude = value.Value.ToBigInteger();
        return value.IsNegative ? -magnitude : magnitude;
    }

    public static uint[] Limbs(BigInteger value, int words)
    {
        var limbs = new uint[words];
        value.WriteLimbs(limbs);
        return limbs;
    }

    // Values for ~: zero, the negative zero (a zero magnitude with the negative flag), small values, and the largest
    // magnitudes of 1, 2 and 33 words, whose ~ needs one more word
    public static (string Value, int Words, bool NegativeZero)[] NotValues() =>
    [
        ("0", 1, false),
        ("0", 1, true),
        ("1", 1, false),
        ("-1", 1, false),
        ("5", 1, false),
        ("-5", 2, false),
        (uint.MaxValue.ToString(), 1, false),
        ((-(BigInteger.One << 32)).ToString(), 2, false),
        (AllOnes(32 * 33).ToString(), 33, false),
        ((-AllOnes(32 * 33)).ToString(), 33, false)
    ];

    public static TheoryData<string, int, bool> NotCases()
    {
        var data = new TheoryData<string, int, bool>();
        foreach (var (value, words, negativeZero) in NotValues())
        {
            data.Add(value, words, negativeZero);
        }

        return data;
    }

    public static void Not(GpuWorkspace workspace, BigInteger value, int words, bool negativeZero)
    {
        var limbs = new uint[words];
        BigInteger.Abs(value).WriteLimbs(limbs);
        using var x = new GpuBigInt(new GpuBigUInt(workspace, limbs), value.Sign < 0 || negativeZero);

        using var result = (~(GpuBigIntExpr)x).Evaluate(workspace);

        Assert.Equal(-value - 1, Read(result));
        Assert.Equal(BigIntWidths.Words(32L * words + 1), result.Value.NumWords);
    }

    public static void AddSubtract(GpuWorkspace workspace, int aWords, int bWords)
    {
        var magnitudeA = AllOnes(32 * aWords);
        var magnitudeB = AllOnes(32 * bWords);
        var words = Math.Max(aWords, bWords) + 1;
        foreach (var signA in new[] { 1, -1 })
        {
            foreach (var signB in new[] { 1, -1 })
            {
                var valueA = signA * magnitudeA;
                var valueB = signB * magnitudeB;
                using var a = Value(workspace, valueA, aWords);
                using var b = Value(workspace, valueB, bWords);

                using var sum = ((GpuBigIntExpr)a + b).Evaluate(workspace);
                using var difference = ((GpuBigIntExpr)a - b).Evaluate(workspace);

                Assert.Equal(valueA + valueB, Read(sum));
                Assert.Equal(valueA - valueB, Read(difference));
                Assert.Equal(words, sum.Value.NumWords);
                Assert.Equal(words, difference.Value.NumWords);
                Assert.False(sum.IsNegative && sum.Value.IsZero());
                Assert.False(difference.IsNegative && difference.Value.IsZero());
            }
        }
    }

    public static void Differences(GpuWorkspace workspace, int aWords, int bWords)
    {
        // Only the top bit of the wider operand differs from an all-ones pattern in the narrower one, so the sign is
        // decided by the top word
        var small = AllOnes(32 * Math.Min(aWords, bWords) - 1);
        var large = BigInteger.One << (32 * Math.Max(aWords, bWords) - 1);
        var (valueA, valueB) = aWords >= bWords ? (large, -small) : (small, -large);
        using var a = Value(workspace, valueA, aWords);
        using var b = Value(workspace, valueB, bWords);
        using var sum = ((GpuBigIntExpr)a + b).Evaluate(workspace);
        Assert.Equal(valueA + valueB, Read(sum));

        // Equal magnitudes with different signs cancel to +0
        var common = AllOnes(32 * Math.Min(aWords, bWords));
        using var c = Value(workspace, -common, aWords);
        using var d = Value(workspace, common, bWords);
        using var zero = ((GpuBigIntExpr)c + d).Evaluate(workspace);
        using var zero2 = ((GpuBigIntExpr)c - (GpuBigIntExpr)c).Evaluate(workspace);
        Assert.True(zero.Value.IsZero() && !zero.IsNegative);
        Assert.True(zero2.Value.IsZero() && !zero2.IsNegative);
    }

    public static void OtherOperators(GpuWorkspace workspace, int aWords, int bWords)
    {
        var valueA = -(AllOnes(32 * aWords) - 12345);
        var valueB = AllOnes(32 * bWords - 3) + 1;
        using var a = Value(workspace, valueA, aWords);
        using var b = Value(workspace, valueB, bWords);
        GpuBigIntExpr x = a;
        GpuBigIntExpr y = b;

        using var product = (x * y).Evaluate(workspace);
        using var quotient = (x / y).Evaluate(workspace);
        using var remainder = (x % y).Evaluate(workspace);
        using var max = GpuBigIntExpr.Max(x, y).Evaluate(workspace);
        using var min = GpuBigIntExpr.Min(x, y).Evaluate(workspace);
        using var chained = ((x + y) / y).Evaluate(workspace);

        // (x + y)^2: the bound (2 * (max + 1) bits) has fewer words than the sum of the operand words, so the product
        // goes through a temporary
        using var square = ((x + y) * (x + y)).Evaluate(workspace);
        var sumWords = Math.Max(aWords, bWords) + 1;
        Assert.Equal(BigIntWidths.Words(2L * (32L * Math.Max(aWords, bWords) + 1)), square.Value.NumWords);
        Assert.True(square.Value.NumWords < 2 * sumWords);
        Assert.Equal((valueA + valueB) * (valueA + valueB), Read(square));

        Assert.Equal(valueA * valueB, Read(product));
        Assert.Equal(aWords + bWords, product.Value.NumWords);
        Assert.Equal(BigInteger.Divide(valueA, valueB), Read(quotient));
        Assert.Equal(aWords, quotient.Value.NumWords);
        Assert.Equal(BigInteger.Remainder(valueA, valueB), Read(remainder));
        Assert.Equal(Math.Min(aWords, bWords), remainder.Value.NumWords);
        Assert.Equal(BigInteger.Max(valueA, valueB), Read(max));
        Assert.Equal(BigInteger.Min(valueA, valueB), Read(min));
        Assert.Equal(Math.Max(aWords, bWords), max.Value.NumWords);
        Assert.Equal(BigInteger.Divide(valueA + valueB, valueB), Read(chained));
    }
}
