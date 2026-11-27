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

    // Mega-kernel carry and add/sub scans exchange state across 32-thread warps and 32-warp shared buffers, so every
    // positive multiple of 32 up to 1024 threads per block is accepted.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:536-538
    //   `blockDim > CooperativeMegaKernelLauncher.MaxThreadsPerBlock`
    //   -> `blockDim >= CooperativeMegaKernelLauncher.MaxThreadsPerBlock` (1024u throws)
    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(32u)]
    [InlineData(64u)]
    [InlineData(96u)]
    [InlineData(256u)]
    [InlineData(512u)]
    [InlineData(768u)]
    [InlineData(1024u)]
    public void ThrowIfInvalidMegaKernelBlockDim_ValidWarpMultiples_DoesNotThrow(uint blockDim)
    {
        var exception = Record.Exception(() =>
            KernelAdvancedMathHandler.ThrowIfInvalidMegaKernelBlockDim(blockDim));

        Assert.Null(exception);
    }

    // Zero, non-multiples of 32, and values above 1024 threads per block break chunk sizing or warp-32 shared scans
    // and must throw ArgumentOutOfRangeException before allocating NTT workspace buffers.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:536-538
    //   `blockDim % CooperativeMegaKernelLauncher.WarpSize != 0` -> removed (260u, 31u, 33u do not throw)
    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(31u)]
    [InlineData(33u)]
    [InlineData(260u)]
    [InlineData(1023u)]
    [InlineData(1025u)]
    [InlineData(1056u)]
    [InlineData(uint.MaxValue)]
    public void ThrowIfInvalidMegaKernelBlockDim_InvalidBlockDims_ThrowsArgumentOutOfRangeException(uint blockDim)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            KernelAdvancedMathHandler.ThrowIfInvalidMegaKernelBlockDim(blockDim));

        Assert.Equal("blockDim", exception.ParamName);
    }

    // Up to StridedColumnMaxTransformLength (65536), strided column NTT accepts blockDim < 256; above 65536,
    // TransposeTileBatches accepts blockDim >= 256.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:566
    //   `transformLength > GpuBigIntNttKernels.StridedColumnMaxTransformLength` -> `>=` (65536 with 32u throws)
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:567
    //   `blockDim < StridedColumnMinBlockDim` -> `<= StridedColumnMinBlockDim` (131072 with 256u throws)
    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(65536, 32u)]
    [InlineData(65536, 96u)]
    [InlineData(65536, 224u)]
    [InlineData(65536, 256u)]
    [InlineData(131072, 256u)]
    [InlineData(131072, 512u)]
    [InlineData(131072, 768u)]
    [InlineData(131072, 1024u)]
    public void ThrowIfStridedColumnBlockDimTooSmall_ValidBlockDimForTransformLength_DoesNotThrow(
        int transformLength, uint blockDim)
    {
        var exception = Record.Exception(() =>
            KernelAdvancedMathHandler.ThrowIfStridedColumnBlockDimTooSmall(blockDim, transformLength));

        Assert.Null(exception);
    }

    // Above StridedColumnMaxTransformLength (65536), TransposeTileBatches moves 32x32 tiles with 8 warps (256
    // threads), so blockDim < 256 must throw ArgumentOutOfRangeException before allocating NTT workspace buffers.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:567
    //   `blockDim < StridedColumnMinBlockDim` -> removed (32u, 96u, 224u at 131072 do not throw)
    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(131072, 32u)]
    [InlineData(131072, 96u)]
    [InlineData(131072, 224u)]
    public void ThrowIfStridedColumnBlockDimTooSmall_SmallBlockDimAboveLimit_ThrowsArgumentOutOfRangeException(
        int transformLength, uint blockDim)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            KernelAdvancedMathHandler.ThrowIfStridedColumnBlockDimTooSmall(blockDim, transformLength));

        Assert.Equal("blockDim", exception.ParamName);
    }
}
