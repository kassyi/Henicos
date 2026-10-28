namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Which implementation <see cref="GSharpKernelFloatProvider"/> runs: the G# kernels on a GPU, or the injected
/// fallback strategy in CPU fallback mode.
/// </summary>
[Collection(TestCollections.GlobalStateSequential)]
public class GSharpKernelFloatProviderDispatchTests
{
    // Every operation of IGpuBigFloatArithmetic; each dispatches as a whole (Pow included).
    private static readonly string[] s_dispatchedOperations =
    [
        nameof(IGpuBigFloatArithmetic.Normalize),
        nameof(IGpuBigFloatArithmetic.Add),
        nameof(IGpuBigFloatArithmetic.Multiply),
        nameof(IGpuBigFloatArithmetic.Divide),
        nameof(IGpuBigFloatArithmetic.Reciprocal),
        nameof(IGpuBigFloatArithmetic.Sqrt),
        nameof(IGpuBigFloatArithmetic.Rsqrt),
        nameof(IGpuBigFloatArithmetic.Exp),
        nameof(IGpuBigFloatArithmetic.Log),
        nameof(IGpuBigFloatArithmetic.Pow),
        nameof(IGpuBigFloatArithmetic.DivideByUInt32),
    ];

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuModeNeverCallsFallback()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        var spy = new RecordingFloatArithmetic();
        var provider = new GSharpKernelFloatProvider(spy);

        var results = RunEveryOperation(workspace, provider);

        // With a GPU the provider runs its own kernels: the fallback is never consulted.
        // mutant: GSharpKernelFloatProvider.cs:47 `is not GSharpKernelProvider || IsCpuFallbackMode` -> `... || !IsCpuFallbackMode`
        // (and the same negation at L76, L164, L198, L233, L272, L311, L390, L409, L429, L445)
        Assert.Empty(spy.Calls);

        // The kernel path computes the same exact values as the reference.
        AssertExactResults(results);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void CpuFallbackModeCallsFallbackOnce()
    {
        // ForceCpuFallback is AsyncLocal, so the override cannot leak into other tests.
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
            var spy = new RecordingFloatArithmetic();
            var provider = new GSharpKernelFloatProvider(spy);

            var results = RunEveryOperation(workspace, provider);

            // In CPU fallback mode each operation is forwarded to the fallback exactly once.
            // mutant: GSharpKernelFloatProvider.cs:47 `||` -> `&&` (and L80, L171, L204, L247, L286, L330, L481, L500,
            // L520)
            // mutant: GSharpKernelFloatProvider.cs:536 `&&` -> `||`
            // mutant: GSharpKernelFloatProvider.cs Pow `return fallbackStrategy.Pow(...)` -> `return
            // GpuBigFloatTranscendentals.Pow(...)` (Pow not forwarded)
            foreach (var operation in s_dispatchedOperations)
            {
                Assert.True(spy.Calls.TryGetValue(operation, out var count), $"{operation} was not forwarded to the fallback.");
                Assert.Equal(1, count);
            }

            Assert.Equal(s_dispatchedOperations.Length, spy.Calls.Count);
            AssertExactResults(results);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    private readonly record struct ExactResults(
        BigInteger NormalizeSig,
        GpuFloatResult Normalize,
        BigInteger AddSig,
        GpuFloatResult Add,
        BigInteger MultiplySig,
        GpuFloatResult Multiply,
        BigInteger DivideSig,
        GpuFloatResult Divide,
        BigInteger DivideByUInt32Sig,
        GpuFloatScalarDivResult DivideByUInt32);

    // Calls each dispatched operation once with a = 4, b = 2 and returns the results that are exact.
    private static ExactResults RunEveryOperation(GpuWorkspace workspace, GSharpKernelFloatProvider provider)
    {
        var context = new GpuBigIntExecutionContext(
            workspace, workspace.MemoryManager, new LegacyProviderArithmeticAdapter(), workspace.Stream);

        using var aSig = new GpuBigUInt(workspace, [4u]);
        using var bSig = new GpuBigUInt(workspace, [2u]);
        using var normalizeSig = new GpuBigUInt(workspace, 4);
        using var addSig = new GpuBigUInt(workspace, 4);
        using var multiplySig = new GpuBigUInt(workspace, 4);
        using var divideSig = new GpuBigUInt(workspace, 4);
        using var scratchSig = new GpuBigUInt(workspace, 4);
        using var divideByUInt32Sig = new GpuBigUInt(workspace, 1);

        var a = new GpuFloatOperand(aSig.Handle, 0);
        var b = new GpuFloatOperand(bSig.Handle, 0);

        var normalize = provider.Normalize(ref context, in a, normalizeSig.Handle);
        var add = provider.Add(ref context, in a, in b, addSig.Handle);
        var multiply = provider.Multiply(ref context, in a, in b, multiplySig.Handle);
        var divide = provider.Divide(ref context, in a, in b, divideSig.Handle);
        provider.Reciprocal(ref context, in a, scratchSig.Handle);
        provider.Sqrt(ref context, in a, scratchSig.Handle);
        provider.Rsqrt(ref context, in a, scratchSig.Handle);
        provider.Exp(ref context, in b, scratchSig.Handle);
        provider.Log(ref context, in b, scratchSig.Handle);
        provider.Pow(ref context, in b, in b, scratchSig.Handle);
        var divideByUInt32 = provider.DivideByUInt32(ref context, in a, 3u, divideByUInt32Sig.Handle);

        return new ExactResults(
            normalizeSig.ToBigInteger(),
            normalize,
            addSig.ToBigInteger(),
            add,
            multiplySig.ToBigInteger(),
            multiply,
            divideSig.ToBigInteger(),
            divide,
            divideByUInt32Sig.ToBigInteger(),
            divideByUInt32);
    }

    // Normalize, Add, Multiply and Divide of small integers are exact; 4 / 3 is 1 remainder 1.
    private static void AssertExactResults(in ExactResults r)
    {
        AssertValue(4, 1, r.NormalizeSig, r.Normalize);
        AssertValue(6, 1, r.AddSig, r.Add);
        AssertValue(8, 1, r.MultiplySig, r.Multiply);
        AssertValue(2, 1, r.DivideSig, r.Divide);
        Assert.Equal(new BigInteger(1), r.DivideByUInt32Sig);
        Assert.Equal(0, r.DivideByUInt32.Exponent);
        Assert.False(r.DivideByUInt32.IsNegative);
        Assert.Equal(1u, r.DivideByUInt32.Remainder);
    }

    private static void AssertValue(BigInteger num, BigInteger den, BigInteger sig, GpuFloatResult res) =>
        Assert.True(
            BigFloatReference.EqualsRational(sig, res.Exponent, res.IsNegative, num, den),
            $"expected {num}/{den}, actual {BigFloatReference.Describe(sig, res.Exponent, res.IsNegative)}");

    /// <summary>
    /// Counts the calls per method name and forwards them to <see cref="CSharpCpuBigFloatArithmetic"/>.
    /// </summary>
    private sealed class RecordingFloatArithmetic : IGpuBigFloatArithmetic
    {
        private static readonly CSharpCpuBigFloatArithmetic s_inner = CSharpCpuBigFloatArithmetic.Instance;

        public Dictionary<string, int> Calls { get; } = [];

        private void Record(string name) => Calls[name] = Calls.GetValueOrDefault(name) + 1;

        public GpuFloatResult Normalize(ref GpuBigIntExecutionContext context, in GpuFloatOperand value, GpuBigIntMemoryHandle resultSig)
        {
            Record(nameof(Normalize));
            return s_inner.Normalize(ref context, in value, resultSig);
        }

        public GpuFloatResult Add(ref GpuBigIntExecutionContext context, in GpuFloatOperand left, in GpuFloatOperand right, GpuBigIntMemoryHandle resultSig)
        {
            Record(nameof(Add));
            return s_inner.Add(ref context, in left, in right, resultSig);
        }

        public GpuFloatResult Multiply(ref GpuBigIntExecutionContext context, in GpuFloatOperand left, in GpuFloatOperand right, GpuBigIntMemoryHandle resultSig)
        {
            Record(nameof(Multiply));
            return s_inner.Multiply(ref context, in left, in right, resultSig);
        }

        public GpuFloatResult Divide(ref GpuBigIntExecutionContext context, in GpuFloatOperand left, in GpuFloatOperand right, GpuBigIntMemoryHandle resultSig)
        {
            Record(nameof(Divide));
            return s_inner.Divide(ref context, in left, in right, resultSig);
        }

        public GpuFloatResult Reciprocal(ref GpuBigIntExecutionContext context, in GpuFloatOperand value, GpuBigIntMemoryHandle resultSig)
        {
            Record(nameof(Reciprocal));
            return s_inner.Reciprocal(ref context, in value, resultSig);
        }

        public GpuFloatResult Sqrt(ref GpuBigIntExecutionContext context, in GpuFloatOperand value, GpuBigIntMemoryHandle resultSig)
        {
            Record(nameof(Sqrt));
            return s_inner.Sqrt(ref context, in value, resultSig);
        }

        public GpuFloatResult Rsqrt(ref GpuBigIntExecutionContext context, in GpuFloatOperand value, GpuBigIntMemoryHandle resultSig)
        {
            Record(nameof(Rsqrt));
            return s_inner.Rsqrt(ref context, in value, resultSig);
        }

        public GpuFloatResult Exp(ref GpuBigIntExecutionContext context, in GpuFloatOperand value, GpuBigIntMemoryHandle resultSig)
        {
            Record(nameof(Exp));
            return s_inner.Exp(ref context, in value, resultSig);
        }

        public GpuFloatResult Log(ref GpuBigIntExecutionContext context, in GpuFloatOperand value, GpuBigIntMemoryHandle resultSig)
        {
            Record(nameof(Log));
            return s_inner.Log(ref context, in value, resultSig);
        }

        public GpuFloatResult Pow(ref GpuBigIntExecutionContext context, in GpuFloatOperand baseValue, in GpuFloatOperand exponentValue, GpuBigIntMemoryHandle resultSig)
        {
            Record(nameof(Pow));
            return s_inner.Pow(ref context, in baseValue, in exponentValue, resultSig);
        }

        public GpuFloatScalarDivResult DivideByUInt32(ref GpuBigIntExecutionContext context, in GpuFloatOperand value, uint divisor, GpuBigIntMemoryHandle resultSig)
        {
            Record(nameof(DivideByUInt32));
            return s_inner.DivideByUInt32(ref context, in value, divisor, resultSig);
        }
    }
}
