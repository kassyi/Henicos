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
    // mutant: GSharpKernelFloatProvider.cs:62 `checked(value.Exponent - shift)` -> `unchecked(...)` (Gpu)
    // mutant: CSharpCpuBigFloatArithmetic.cs:92 `checked(value.Exponent - shift)` -> `unchecked(...)` (Cpu)
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
