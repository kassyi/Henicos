namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// <see cref="GpuBigIntDivRem.ModPow"/> under the CPU fallback, on both sides of
/// <see cref="GpuWorkspace.PolyAlgorithmThreshold"/>, against <see cref="BigInteger.ModPow"/> of the inputs the test
/// holds. The workspace memory is host memory then, so no CUDA call may be made.
/// </summary>
/// <remarks>
/// <see cref="GpuKernelLoader.ForceCpuFallback"/> is AsyncLocal, so each test sets and restores it itself
/// (<see cref="CpuFallbackScope"/>).
/// </remarks>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class ModPowCpuFallbackTests
{
    // 4 words (128 bits) are below the threshold of 1024 bits, 64 words (2048 bits) above it
    public static TheoryData<int> Widths => [4, 64];

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(Widths))]
    public void ModPow_CpuFallbackRandomOperands_MatchesBigInteger(int words)
    {
        // Full-width base and modulus (the base above the modulus, so it is reduced), a two-word exponent
        var random = new Random(1338 + words);
        var baseValue = RandomValue(random, words * 32);
        var exponent = RandomValue(random, 64);
        var modulus = RandomValue(random, words * 32 - 1) | 1;

        // The power modulo the modulus. Before the fix the width below the threshold launched the CUDA mega-kernel
        // on host memory and threw CudaException.
        // mutant: GpuBigIntDivRem.ModPow.cs `if (GpuKernelLoader.IsCpuFallbackMode)` -> `if (false)`
        // mutant: CSharpCpuBigIntProvider.cs ModPow `BigInteger.ModPow(b, e, m)` -> `BigInteger.ModPow(b, e + 1, m)`
        Assert.Equal(BigInteger.ModPow(baseValue, exponent, modulus), RunModPow(words, baseValue, exponent, modulus));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(4, 3, 3, 5)]
    [InlineData(4, 7, 0, 11)]
    [InlineData(4, 99, 5, 1)]
    [InlineData(4, 0, 0, 7)]
    [InlineData(64, 7, 0, 11)]
    [InlineData(64, 99, 5, 1)]
    public void ModPow_CpuFallbackSmallOperands_MatchesBigInteger(int words, int baseValue, int exponent, int modulus)
    {
        // Exponent 0 is 1 modulo the modulus (0^0 included), modulus 1 gives 0
        Assert.Equal(BigInteger.ModPow(baseValue, exponent, modulus), RunModPow(words, baseValue, exponent, modulus));
    }

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [MemberData(nameof(Widths))]
    public void ModPow_CpuFallbackZeroModulus_ThrowsDivideByZero(int words)
    {
        // A zero modulus throws DivideByZeroException, as on the GPU paths and in BigInteger.ModPow
        // mutant: GpuBigIntDivRem.ModPow.cs `if (GpuKernelLoader.IsCpuFallbackMode)` -> `if (false)` -> CudaException
        // for 64 words (the 4-word path checks the modulus before its first launch)
        Assert.Throws<DivideByZeroException>(() => RunModPow(words, 3, 3, 0));
    }

    private static BigInteger RunModPow(int words, BigInteger baseValue, BigInteger exponent, BigInteger modulus)
    {
        using var fallback = CpuFallbackScope.Enter();
        using var workspace = new GpuWorkspace(words * 4);
        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, words);
        using var gpuExponent = GpuBigUInt.FromBigInteger(workspace, exponent, words);
        using var gpuModulus = GpuBigUInt.FromBigInteger(workspace, modulus, words);
        using var result = new GpuBigUInt(workspace, words);
        GpuBigIntDivRem.ModPow(gpuBase, gpuExponent, gpuModulus, result);
        return result.ToBigInteger();
    }

    // A value of exactly bits bits (the top bit set)
    private static BigInteger RandomValue(Random random, int bits)
    {
        var bytes = new byte[(bits + 7) / 8 + 1];
        random.NextBytes(bytes);
        bytes[^1] = 0;
        var value = new BigInteger(bytes) & ((BigInteger.One << bits) - 1);
        return value | (BigInteger.One << (bits - 1));
    }
}
