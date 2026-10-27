#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

/// <summary>
/// <see cref="IGpuBasicMathProvider.ReadLeadingBits"/> of the GPU and the CPU provider against <see cref="BigInteger"/>.
/// </summary>
[Category(TestCategory.Stdlib)]
[Collection(TestCollections.GlobalStateSequential)]
public class ReadLeadingBitsTests
{
    // Bit lengths around the word boundaries of the three words the kernel reads, with leading zero words above.
    public static TheoryData<int, int> Cases()
    {
        var data = new TheoryData<int, int>();
        foreach (var bitLength in new[] { 0, 1, 2, 31, 32, 33, 63, 64, 65, 95, 96, 97, 2048 })
        {
            data.Add(bitLength, (bitLength + 31) / 32 + 2);
        }

        return data;
    }

    // The bit length and the top 64 bits (top bit at bit 63, missing low bits 0) agree with BigInteger.
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(Cases))]
    // mutant: GpuBigIntCmpKernels.cu.cs LeadingBitsKernel `wordIndex >= 2 ? dA[wordIndex - 2] : 0ul` -> `0ul`
    // (bit lengths 65, 95, 97, 2048 lose the low bits from the third word)
    // mutant: GpuBigIntCmpKernels.cu.cs LeadingBitsKernel `(middle << leadingZeros)` -> `(middle << (leadingZeros + 1))`
    public void Gpu_MatchesBigInteger(int bitLength, int words)
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        AssertLeadingBits(GSharpKernelProvider.Instance, workspace, bitLength, words);
    }

    // The CPU provider (used in CPU fallback mode) gives the same values.
    [Theory]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(Cases))]
    // mutant: CSharpCpuBigIntProvider.cs ReadLeadingBits `val << (64 - bitLength)` -> `val << (63 - bitLength)`
    public void Cpu_MatchesBigInteger(int bitLength, int words)
    {
        // ForceCpuFallback is AsyncLocal, so the override cannot leak into other tests.
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

            AssertLeadingBits(CSharpCpuBigIntProvider.Instance, workspace, bitLength, words);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    private static void AssertLeadingBits(IGpuExecutionProvider provider, GpuWorkspace workspace, int bitLength, int words)
    {
        var value = Value(bitLength);
        using var buffer = new GpuBigUInt(workspace, ToWords(value, words));

        provider.ReadLeadingBits(workspace, buffer, out var actualLength, out var actualLeading);

        var expectedLeading = bitLength >= 64 ? value >> (bitLength - 64) : value << (64 - bitLength);
        Assert.Equal(bitLength, actualLength);
        Assert.Equal((ulong)expectedLeading, actualLeading);
    }

    // A value of exactly `bitLength` bits with an irregular bit pattern (seeded).
    private static BigInteger Value(int bitLength)
    {
        if (bitLength == 0)
        {
            return BigInteger.Zero;
        }

        var bytes = new byte[(bitLength + 7) / 8 + 1];
        new Random(628 + bitLength).NextBytes(bytes);
        var value = new BigInteger(bytes, isUnsigned: true) & ((BigInteger.One << bitLength) - 1);
        return value | (BigInteger.One << (bitLength - 1));
    }

    private static uint[] ToWords(BigInteger value, int words)
    {
        var result = new uint[words];
        for (var i = 0; i < words; i++)
        {
            result[i] = (uint)(value >> (32 * i) & uint.MaxValue);
        }

        return result;
    }
}
