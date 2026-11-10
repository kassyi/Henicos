#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// Properties of the operations with independent operand and result widths (BigIntWideOps), against a BigInteger
// oracle, at operand widths 1, 2, 33, 64 and 1025 words and the unequal pairs between them:
// - a + b into Words(BigIntWidths.Add) words is exact, with the carry chain through every word;
// - a - b (a >= b) into the width of a is exact, also when b is wider than a with zero high words;
// - a - b below zero, a sum or difference that does not fit, and a value that does not fit its Fit width are domain
//   errors (OverflowException at the next read on the GPU, at the call on the CPU fallback), reported once;
// - a difference whose operands both exceed the result width but whose value fits is exact (the kernel computes
//   every word, not only the result's);
// - &, |, ^ and ~ follow BigInteger on unequal widths, with a missing word read as zero and ~ over the words of a;
// - every word of the result buffer is written (it is prefilled with 0xDEADBEEF).

/// <summary>The wide operations on the GPU.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class BigIntWideOpsTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public BigIntWideOpsTests()
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

    public static TheoryData<int, int> WidthPairs() => WideCases.WidthPairs();

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.Boundary)]
    public void Add_IntoTheBound_IsExact(int aWords, int bWords) =>
        WideCases.AddIntoTheBound(_workspace, aWords, bWords, static ws => ws.ThrowIfDeviceErrors());

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.HappyPath)]
    public void Subtract_NotBelowZero_IsExact(int aWords, int bWords) =>
        WideCases.SubtractNotBelowZero(_workspace, aWords, bWords, static ws => ws.ThrowIfDeviceErrors());

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.SadPath)]
    public void OutOfDomain_IsReportedOnceAtTheNextRead(int aWords, int bWords)
    {
        foreach (var shape in WideCases.OutOfDomainShapes.Where(s => s.Applies(aWords, bWords)))
        {
            using var a = WideCases.Operand(_workspace, shape.A(aWords, bWords), aWords);
            using var b = WideCases.Operand(_workspace, shape.B(aWords, bWords), bWords);
            using var result = new GpuBigUInt(_workspace, shape.ResultWords(aWords, bWords));

            BigIntWideOps.AddSubtract(a, b, result, shape.Subtract);

            var error = Assert.Throws<OverflowException>(() => result.ToHostArray());
            Assert.Contains(shape.Message, error.Message);
            _workspace.ThrowIfDeviceErrors();
        }
    }

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.Boundary)]
    public void Bitwise_FollowsBigInteger(int aWords, int bWords) =>
        WideCases.Bitwise(_workspace, aWords, bWords, static ws => ws.ThrowIfDeviceErrors());

    [Theory]
    [InlineData(1)]
    [InlineData(33)]
    [InlineData(1025)]
    [Scenario(TestScenario.SadPath)]
    public void CheckFits_ReportsAValueWiderThanTheWords(int words)
    {
        using var fits = WideCases.Operand(_workspace, WideCases.AllOnes(words), words + 3);
        BigIntWideOps.CheckFits(fits, words);
        _workspace.ThrowIfDeviceErrors();

        using var wider = WideCases.Operand(_workspace, BigInteger.One << (32 * (words + 2)), words + 3);
        BigIntWideOps.CheckFits(wider, words);
        Assert.Throws<OverflowException>(() => _workspace.ThrowIfDeviceErrors());
        _workspace.ThrowIfDeviceErrors();
    }
}

/// <summary>The wide operations on the CPU fallback.</summary>
[Category(TestCategory.Stdlib)]
[TestTarget(ExecutionTarget.CpuExecution)]
public sealed class BigIntWideOpsCpuFallbackTests
{
    public static TheoryData<int, int> WidthPairs() => WideCases.WidthPairs();

    [Theory]
    [MemberData(nameof(WidthPairs))]
    [Scenario(TestScenario.Boundary)]
    public void WideOps_CpuFallbackWithVaryingWords_FollowsTheSameRules(int aWords, int bWords)
    {
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(1 << 16);
            static void NoCheck(GpuWorkspace ws)
            {
            }

            WideCases.AddIntoTheBound(workspace, aWords, bWords, NoCheck);
            WideCases.SubtractNotBelowZero(workspace, aWords, bWords, NoCheck);
            WideCases.Bitwise(workspace, aWords, bWords, NoCheck);

            foreach (var shape in WideCases.OutOfDomainShapes.Where(s => s.Applies(aWords, bWords)))
            {
                using var a = WideCases.Operand(workspace, shape.A(aWords, bWords), aWords);
                using var b = WideCases.Operand(workspace, shape.B(aWords, bWords), bWords);
                using var result = new GpuBigUInt(workspace, shape.ResultWords(aWords, bWords));
                Assert.Throws<OverflowException>(() => BigIntWideOps.AddSubtract(a, b, result, shape.Subtract));
            }

            using var wider = WideCases.Operand(workspace, BigInteger.One << (32 * aWords), aWords + 1);
            Assert.Throws<OverflowException>(() => BigIntWideOps.CheckFits(wider, aWords));
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }
}

/// <summary>One input shape whose result is out of the domain of the result buffer.</summary>
internal sealed record OutOfDomainShape(
    string Name,
    bool Subtract,
    Func<int, int, bool> Applies,
    Func<int, int, BigInteger> A,
    Func<int, int, BigInteger> B,
    Func<int, int, int> ResultWords,
    string Message);

/// <summary>Inputs, oracle checks and shapes shared by the GPU and CPU fallback tests.</summary>
internal static class WideCases
{
    private const uint Fill = 0xDEADBEEFu;

    private static readonly int[] s_widths = [1, 2, 33, 64, 1025];

    public static readonly OutOfDomainShape[] OutOfDomainShapes =
    [
        // The sum of two all-ones values into the wider operand's width: the carry leaves it
        new("add carry", false, static (_, _) => true, static (a, _) => AllOnes(32 * a),
            static (_, b) => AllOnes(32 * b), static (a, b) => Math.Max(a, b), "sum does not fit"),
        // 0 - 1 through every word of the wider operand
        new("below zero", true, static (_, _) => true, static (_, _) => BigInteger.Zero,
            static (_, _) => BigInteger.One, static (a, b) => Math.Max(a, b), "below zero"),
        // b's top bit is the only difference: below zero although the low words of a are all ones
        new("below zero by the top word", true, static (_, _) => true,
            static (a, b) => AllOnes(32 * Math.Min(a, b) - 1), static (_, b) => BigInteger.One << (32 * b - 1),
            static (a, b) => Math.Max(a, b), "below zero"),
        // A difference that needs every word of a, into one word less
        new("difference does not fit", true, static (a, _) => a > 1, static (a, _) => AllOnes(32 * a),
            static (_, _) => BigInteger.One, static (a, _) => a - 1, "does not fit")
    ];

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

    public static GpuBigUInt Operand(GpuWorkspace workspace, BigInteger value, int words)
    {
        var limbs = new uint[words];
        value.WriteLimbs(limbs);
        return new GpuBigUInt(workspace, limbs);
    }

    public static void AddIntoTheBound(GpuWorkspace workspace, int aWords, int bWords, Action<GpuWorkspace> check)
    {
        var aValue = AllOnes(32 * aWords);
        var bValue = AllOnes(32 * bWords);
        using var a = Operand(workspace, aValue, aWords);
        using var b = Operand(workspace, bValue, bWords);
        var words = BigIntWidths.Words(BigIntWidths.Add(32L * aWords, 32L * bWords));
        using var result = Filled(workspace, words);

        BigIntWideOps.AddSubtract(a, b, result, subtract: false);

        Assert.Equal(Limbs(aValue + bValue, words), result.ToHostArray());
        check(workspace);
    }

    public static void SubtractNotBelowZero(GpuWorkspace workspace, int aWords, int bWords,
        Action<GpuWorkspace> check)
    {
        // a has its top bit set, b is smaller and fills the low words: the borrow chain runs through every word of a.
        // b may be declared wider than a; its high words are zero.
        var aValue = BigInteger.One << (32 * aWords - 1);
        var bValue = AllOnes(32 * Math.Min(aWords, bWords) - 1);
        using var a = Operand(workspace, aValue, aWords);
        using var b = Operand(workspace, bValue, bWords);
        using var result = Filled(workspace, aWords);

        BigIntWideOps.AddSubtract(a, b, result, subtract: true);
        Assert.Equal(Limbs(aValue - bValue, aWords), result.ToHostArray());

        // Both operands exceed a one-word result, but the difference fits in it
        using var a2 = Operand(workspace, (BigInteger.One << (32 * aWords + 5)) + 7, aWords + 1);
        using var b2 = Operand(workspace, (BigInteger.One << (32 * aWords + 5)) + 3, aWords + 1);
        using var small = Filled(workspace, 1);
        BigIntWideOps.AddSubtract(a2, b2, small, subtract: true);
        Assert.Equal([4u], small.ToHostArray());
        check(workspace);
    }

    public static void Bitwise(GpuWorkspace workspace, int aWords, int bWords, Action<GpuWorkspace> check)
    {
        var aValue = Pattern(aWords, 0x9E3779B9u);
        var bValue = Pattern(bWords, 0x7F4A7C15u);
        using var a = Operand(workspace, aValue, aWords);
        using var b = Operand(workspace, bValue, bWords);

        var cases = new (int Operation, long Bits, BigInteger Expected)[]
        {
            (GpuBigIntWideKernels.BitwiseAnd, BigIntWidths.And(32L * aWords, 32L * bWords), aValue & bValue),
            (GpuBigIntWideKernels.BitwiseOr, BigIntWidths.Or(32L * aWords, 32L * bWords), aValue | bValue),
            (GpuBigIntWideKernels.BitwiseXor, BigIntWidths.Xor(32L * aWords, 32L * bWords), aValue ^ bValue),
            (GpuBigIntWideKernels.BitwiseNot, BigIntWidths.UnsignedNot(32L * aWords), AllOnes(32 * aWords) ^ aValue)
        };

        foreach (var (operation, bits, expected) in cases)
        {
            // Two words more than the bound: they must be cleared
            var words = BigIntWidths.Words(bits) + 2;
            using var result = Filled(workspace, words);
            BigIntWideOps.Bitwise(a, b, result, operation);
            Assert.Equal(Limbs(expected, words), result.ToHostArray());
        }

        check(workspace);
    }

    private static BigInteger Pattern(int words, uint seed)
    {
        var limbs = new uint[words];
        var x = seed;
        for (var i = 0; i < words; i++)
        {
            x = x * 1664525u + 1013904223u;
            limbs[i] = x;
        }

        return new BigInteger(limbs.SelectMany(BitConverter.GetBytes).ToArray(), isUnsigned: true);
    }

    private static GpuBigUInt Filled(GpuWorkspace workspace, int words) =>
        new(workspace, Enumerable.Repeat(Fill, words).ToArray());

    private static uint[] Limbs(BigInteger value, int words)
    {
        Assert.True(value.Sign >= 0 && value.GetBitLength() <= 32L * words, "The oracle value must fit the buffer.");
        var limbs = new uint[words];
        value.WriteLimbs(limbs);
        return limbs;
    }
}
