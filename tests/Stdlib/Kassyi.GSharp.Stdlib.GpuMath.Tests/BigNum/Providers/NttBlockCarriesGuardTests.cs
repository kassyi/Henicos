using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Providers;

/// <summary>
/// Tests the host check that a block-carry buffer holds one carry for every chunk the NTT carry kernel resolves.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
public class NttBlockCarriesGuardTests
{
    // A product of maxProductWords words resolved in chunkSize-word chunks writes ceil(maxProductWords / chunkSize)
    // carries; a buffer of exactly that many words is enough. The cases are the ModPow products of n = 99 (511 words),
    // n = 100 (516 words) and n = 200 (1016 words) in 256-word chunks, and a product that fills its last chunk exactly.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs
    //   ThrowIfBlockCarriesTooSmall `blockCarryWords < carryCount` -> `blockCarryWords <= carryCount` (every case fails)
    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(2, 511, 256)]
    [InlineData(3, 516, 256)]
    [InlineData(4, 1016, 256)]
    [InlineData(2, 512, 256)]
    [InlineData(1, 1024, 1024)]
    public void ThrowIfBlockCarriesTooSmall_AcceptsOneWordPerChunk(int blockCarryWords, int maxProductWords,
        int chunkSize)
    {
        var exception = Record.Exception(() =>
            KernelAdvancedMathHandler.ThrowIfBlockCarriesTooSmall(blockCarryWords, maxProductWords, chunkSize));

        Assert.Null(exception);
    }

    // One word fewer than the number of chunks lets the kernel write past the buffer, so the check throws. The first
    // case is #606: the 2 words sized for 1024-word chunks against the 3 chunks of 256 words of a 516-word product.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs
    //   ThrowIfBlockCarriesTooSmall `(maxProductWords + chunkSize - 1) / chunkSize` -> `maxProductWords / chunkSize`
    //   (every case fails: a partly filled last chunk is no longer counted)
    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(2, 516, 256)]
    [InlineData(3, 1016, 256)]
    [InlineData(1, 257, 256)]
    [InlineData(0, 1, 1024)]
    public void ThrowIfBlockCarriesTooSmall_RejectsFewerWordsThanChunks(int blockCarryWords, int maxProductWords,
        int chunkSize)
    {
        Assert.Throws<InvalidOperationException>(() =>
            KernelAdvancedMathHandler.ThrowIfBlockCarriesTooSmall(blockCarryWords, maxProductWords, chunkSize));
    }
}
