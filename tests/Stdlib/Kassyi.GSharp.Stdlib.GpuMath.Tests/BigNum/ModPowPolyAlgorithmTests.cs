using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class ModPowPolyAlgorithmTests : IDisposable
{
    private readonly GpuTestWorkspace _testWorkspace;

    public ModPowPolyAlgorithmTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _testWorkspace = new GpuTestWorkspace(4096, GpuExecutionEngine.GSharpAot);
    }

    private GpuWorkspace Context => _testWorkspace.Workspace;

    public void Dispose() => _testWorkspace.Dispose();

    private static BigInteger GenerateRandomBigInteger(int bits, Random rng)
    {
        var bytes = new byte[(bits + 7) / 8];
        rng.NextBytes(bytes);

        var positiveBytes = new byte[bytes.Length + 1];
        Array.Copy(bytes, positiveBytes, bytes.Length);

        int excessBits = (bytes.Length * 8) - bits;
        if (excessBits > 0)
        {
            positiveBytes[bytes.Length - 1] &= (byte)(0xFF >> excessBits);
        }

        return new BigInteger(positiveBytes);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)] // Boundary of PolyAlgorithmThreshold (uses ModPowNttHostController MegaKernel)
    [InlineData(2048)] // Crosses over to ModPowZeroSyncController
    [InlineData(4096)] // Crosses over to ModPowZeroSyncController
    public void ModPow_PolyAlgorithmCrossOver_MatchesBigIntegerReference(int bitLength)
    {
        CudaCapability.EnsureGpuAvailable();

        // Arrange: Generate random BigIntegers.
        // For >1024-bit host-loop crossover cases, use a 64-bit (2-word) exponent to verify full-width
        // 2048/4096-bit multiplications and Barrett reductions across word boundaries without thousands of host-loop iterations.
        int expBitLength = bitLength <= Context.PolyAlgorithmThreshold ? bitLength / 2 : 64;
        var rng = new Random(1337 + bitLength);
        var baseValue = GenerateRandomBigInteger(bitLength, rng);
        var expValue = GenerateRandomBigInteger(expBitLength, rng);
        var modValue = GenerateRandomBigInteger(bitLength, rng);

        // Ensure mod is odd for Montgomery reduction (as a general strictness check, though ModPow should handle it)
        if (modValue.IsEven)
        {
            modValue += 1;
        }

        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        var numWords = (bitLength + 31) / 32;

        using var gpuBase = GpuBigUInt.FromBigInteger(Context, baseValue, numWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(Context, expValue, numWords);
        using var gpuMod = GpuBigUInt.FromBigInteger(Context, modValue, numWords);
        using var gpuResult = new GpuBigUInt(Context, numWords);

        // Act
        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

        // Assert
        var actual = gpuResult.ToBigInteger();
        Assert.Equal(expected, actual);
    }

    // ModPowNttHostController launches ModPowNttMegaKernel with one block sized by ChooseBlockDim (256 up to 512
    // points, 512 at 1024 points, and the largest block that fits above, 768 on sm_86). The kernel resolves the
    // carries of each product in chunks of one block (BlockDimension.X), writing one block carry per chunk into
    // BlockCarries. Its largest product (the Barrett step) has 2n + (3n + 16) = 5n + 16 words: n = 99 gives 511 words
    // (1 chunk of 512), n = 100 gives 516 words (1 chunk of 768), n = 200 gives 1016 words (2 chunks of 768, more than
    // a 1024-word chunk allocation holds), and n = 3270 gives 16366 words (22 chunks of 768).
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/
    //   CooperativeMegaKernelLauncher.cs:473 `ChunkSize = carryChunkSize;` -> `ChunkSize = 1024;`
    //   input: numWords 200 and 3270 (ThrowIfBlockCarriesTooSmall throws before the launch)
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntDivRemKernels.ModPow.cu.cs:88
    //   `int numBlocks = (numWordsResult + chunkSize - 1) / chunkSize;` -> `int numBlocks = 1;`
    //   input: numWords 3270 (the carries across chunks 1..20 are lost and the result differs from BigInteger.ModPow)
    // numWords 99 transforms 1024 points as one shared-memory row on 512 threads (one butterfly per thread); numWords
    // 100 and 200 transform 2048 points as 32 x 64 (768 threads batch 24 rows of 32 points and 12 rows of 64); numWords
    // 3270 transforms 2^15 = 128 x 256 points (N1 != N2, column read).
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntNttKernels.cu.cs:377
    //   `args.ColumnStride = args.Grid.N2;` -> `args.Grid.N1`
    //   input: numWords 3270
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntNttKernels.SharedRow.cu.cs:80
    //   `firstRow + slot * GridDimension.X` -> `firstRow + slot`
    //   input: numWords 3270
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(3270)]
    public void ModPow_NttPathCarryChunksOfLaunchBlock_MatchesBigIntegerReference(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();
        AssertModPowNttMatchesBigIntegerReference(numWords);
    }

    // ModPowNttHostController launches one block (smCount: 1), so ChooseBlockDim selects 256 threads for 48 words
    // (M = 512 <= 2 * 1 * 256), 512 for 99 words (M = 1024 <= 2 * 1 * 512), and the largest block that fits for 100
    // words (M = 2048; 768 on sm_86 with 66 registers per thread).
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/ModPowNttHostController.cs:56
    //   `gridDim: 1, blockDim: shape.BlockDim` -> `blockDim: 256`
    //   input: numWords 99 and 100
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/ModPowNttHostController.cs:50
    //   `smCount: 1` -> `smCount: null`
    //   input: numWords 99 and 100 on a multi-SM GPU
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/CooperativeMegaKernelLauncher.cs:321
    //   `var blockDim = ChooseBlockDim(transformLength, effectiveSmCount, largestFitting, candidates);`
    //   -> `largestFitting`
    //   input: numWords 48 and 99
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(48, 256)]
    [InlineData(99, 512)]
    [InlineData(100, 0)]
    public void ModPow_NttPath_ChoosesBlockSizeOfTheRule(int numWords, int expectedBlockSize)
    {
        CudaCapability.EnsureGpuAvailable();
        using var workspace = new GpuWorkspace(Math.Max(4096, numWords * 16), GpuExecutionEngine.GSharpAot);
        workspace.PolyAlgorithmThreshold = numWords * 32;

        var seed = 606 + numWords;
        var random = new Random(seed);
        var modValue = GenerateFullWidthBigInteger(random, numWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, numWords * 32) % modValue;
        var expValue = GenerateFullWidthBigInteger(random, 64);

        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, numWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(workspace, expValue, 2);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, numWords);
        using var gpuResult = new GpuBigUInt(workspace, numWords);

        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

        Assert.True(expected == gpuResult.ToBigInteger(), $"seed {seed} mismatch");
        var sharedMemBytes = CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(
            CooperativeMegaKernelLauncher.TransformLengthFor(5 * numWords + 16));
        var expectedBlockDim = expectedBlockSize != 0
            ? (uint)expectedBlockSize
            : CooperativeMegaKernelLauncher.ResolveCooperativeBlockDim(
                workspace,
                static () => GpuBigIntDivRemKernels.ResolveModPowNttMegaKernel(),
                CooperativeMegaKernelLauncher.ModPowKernelName,
                sharedMemBytes);
        Assert.Equal(expectedBlockDim, workspace.LastModPowNttBlockDim);
    }

    // 6600 words transforms 2^17 = 256 x 512 points (N1 != N2) on one block of the largest fitting block size (768
    // threads on sm_86) through the transposes.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntNttKernels.cu.cs:425 / :469
    //   `TransposeTiles(buffer, temporaryBuffer, length2, length1);` -> `(..., length1, length2)`
    //   input: numWords 6600
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.Boundary)]
    public void ModPow_NttPathThroughTransposes_MatchesBigIntegerReference()
    {
        CudaCapability.EnsureGpuAvailable();
        AssertModPowNttMatchesBigIntegerReference(6600);
    }

    private static void AssertModPowNttMatchesBigIntegerReference(int numWords)
    {
        using var workspace = new GpuWorkspace(Math.Max(4096, numWords * 16), GpuExecutionEngine.GSharpAot);
        // Send numWords-word operands to the NTT MegaKernel (ModPowNttHostController) instead of the zero-sync loop.
        workspace.PolyAlgorithmThreshold = numWords * 32;

        var seed = 606 + numWords;
        var random = new Random(seed);
        // The top bit of the modulus is set so that every one of its numWords words is significant.
        var modValue = GenerateFullWidthBigInteger(random, numWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, numWords * 32) % modValue;
        // A 2-word exponent keeps the run at 64 square-and-multiply steps, each with full-width NTT products.
        var expValue = GenerateFullWidthBigInteger(random, 64);

        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, numWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(workspace, expValue, 2);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, numWords);
        using var gpuResult = new GpuBigUInt(workspace, numWords);

        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

        var actual = gpuResult.ToBigInteger();
        // The result equals the reference modular power computed from the host-side inputs.
        Assert.True(expected == actual, $"seed {seed}: expected {expected}, actual {actual}");
    }

    private static BigInteger GenerateFullWidthBigInteger(Random random, int bits)
    {
        var bytes = new byte[bits / 8 + 1];
        random.NextBytes(bytes);
        bytes[^1] = 0; // sign byte: keep the value non-negative
        bytes[^2] |= 0x80; // top bit of the value
        return new BigInteger(bytes);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ModPow_ZeroOneAndExtrema_MatchesBigIntegerOrThrowsExpectedExceptions()
    {
        CudaCapability.EnsureGpuAvailable();
        
        // Arrange
        var modValue = BigInteger.Parse("123456789012345678901234567890123456789012345678901234567891"); // odd
        var numWords = 10;
        
        using var gpuMod = GpuBigUInt.FromBigInteger(Context, modValue, numWords);
        using var gpuResult = new GpuBigUInt(Context, numWords);

        // 1. Base 0
        using var gpuBase0 = GpuBigUInt.FromBigInteger(Context, BigInteger.Zero, numWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(Context, new BigInteger(5), numWords);
        GpuBigIntDivRem.ModPow(gpuBase0, gpuExp, gpuMod, gpuResult);
        Assert.Equal(BigInteger.ModPow(BigInteger.Zero, 5, modValue), gpuResult.ToBigInteger());

        // 2. Exponent 0
        using var gpuBase5 = GpuBigUInt.FromBigInteger(Context, new BigInteger(5), numWords);
        using var gpuExp0 = GpuBigUInt.FromBigInteger(Context, BigInteger.Zero, numWords);
        GpuBigIntDivRem.ModPow(gpuBase5, gpuExp0, gpuMod, gpuResult);
        Assert.Equal(BigInteger.ModPow(5, BigInteger.Zero, modValue), gpuResult.ToBigInteger());
        
        // 3. Base 1
        using var gpuBase1 = GpuBigUInt.FromBigInteger(Context, BigInteger.One, numWords);
        GpuBigIntDivRem.ModPow(gpuBase1, gpuExp, gpuMod, gpuResult);
        Assert.Equal(BigInteger.ModPow(BigInteger.One, 5, modValue), gpuResult.ToBigInteger());

        // 4. Modulus 0 (DivideByZeroException)
        using var gpuMod0 = GpuBigUInt.FromBigInteger(Context, BigInteger.Zero, numWords);
        Assert.Throws<DivideByZeroException>(() => GpuBigIntDivRem.ModPow(gpuBase5, gpuExp, gpuMod0, gpuResult));

        // 5. Size Mismatch (ArgumentException)
        using var gpuMismatch = new GpuBigUInt(Context, numWords + 1);
        Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.ModPow(gpuMismatch, gpuExp, gpuMod, gpuResult));
    }

    // ExecuteModPowNttMegaKernel must reject invalid blockDim before NttWorkspaceBuffers allocation or kernel launch;
    // otherwise n = 64 with blockDim = 260 returns a wrong result without throwing.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:270
    //         `ThrowIfInvalidMegaKernelBlockDim(blockDim);` -> removed
    // input: n = 64, targetBits = 96 * 64 + 512, exp = 65537, blockDim = 260
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(260u)]
    [InlineData(0u)]
    [InlineData(31u)]
    [InlineData(33u)]
    [InlineData(1025u)]
    [InlineData(1056u)]
    public void ExecuteModPowNttMegaKernel_InvalidBlockDim_ThrowsArgumentOutOfRangeException(uint blockDim)
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 64;
        const int TargetBits = 96 * NumWords + 512;
        var (gpuBase, gpuExp, gpuMod, gpuRecip, gpuResult, _) =
            CreateModPowNttMegaKernelTestInputs(Context, NumWords, TargetBits, new BigInteger(65537));
        using (gpuBase)
        using (gpuExp)
        using (gpuMod)
        using (gpuRecip)
        using (gpuResult)
        {
            var beforeStats = Context.GetMemoryStatistics();
            var handler = new KernelAdvancedMathHandler();
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                handler.ExecuteModPowNttMegaKernel(
                    Context, gpuBase, gpuResult, gpuMod, gpuRecip, gpuExp,
                    NumWords, TargetBits, gridDim: 1, blockDim: blockDim));

            Assert.Equal("blockDim", exception.ParamName);
            // mutant: KernelAdvancedMathHandler.cs:270 `ThrowIfInvalidMegaKernelBlockDim(blockDim);` moved after the
            //         first RentTemp (260u then throws only after renting)
            var afterStats = Context.GetMemoryStatistics();
            Assert.Equal(beforeStats.PeakLiveBuffers, afterStats.PeakLiveBuffers);
            Assert.Equal(beforeStats.DriverAllocations, afterStats.DriverAllocations);
        }
    }

    // A block size that passes the warp rules can still be too large for the registers of the ModPow kernel (1024
    // threads on an sm_86 device): the cooperative launch then fails with CUDA error 720 after the buffers are rented.
    // The method must reject it with ArgumentOutOfRangeException before renting.
    // mutant: KernelAdvancedMathHandler.cs:279 `ThrowIfModPowBlockDimDoesNotFit(` call removed (CudaException)
    // input: n = 64, blockDim = 1024 (skipped on a device where one block of 1024 threads fits)
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void ExecuteModPowNttMegaKernel_BlockDimTooLargeForTheKernel_ThrowsBeforeRenting()
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 64;
        const int TargetBits = 96 * NumWords + 512;
        const uint BlockDim = 1024u;
        var transformLength = CooperativeMegaKernelLauncher.TransformLengthFor(2 * NumWords + (TargetBits + 31) / 32);
        var sharedMemBytes = CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(transformLength);
        int blocksPerSm;
        using (new GpuKernelLoader.CudaContextScope(Context.IsolatedContext.Handle))
        {
            CudaException.ThrowIfError(CudaDriverApi.cuOccupancyMaxActiveBlocksPerMultiprocessor(
                out blocksPerSm, GpuBigIntDivRemKernels.ResolveModPowNttMegaKernel(), (int)BlockDim, sharedMemBytes));
        }

        Skip.If(blocksPerSm > 0, "One block of 1024 threads of the ModPow kernel fits on this device.");

        var (gpuBase, gpuExp, gpuMod, gpuRecip, gpuResult, _) =
            CreateModPowNttMegaKernelTestInputs(Context, NumWords, TargetBits, new BigInteger(65537));
        using (gpuBase)
        using (gpuExp)
        using (gpuMod)
        using (gpuRecip)
        using (gpuResult)
        {
            var beforeStats = Context.GetMemoryStatistics();
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                new KernelAdvancedMathHandler().ExecuteModPowNttMegaKernel(
                    Context, gpuBase, gpuResult, gpuMod, gpuRecip, gpuExp,
                    NumWords, TargetBits, gridDim: 1, blockDim: BlockDim));

            Assert.Equal("blockDim", exception.ParamName);
            var afterStats = Context.GetMemoryStatistics();
            Assert.Equal(beforeStats.PeakLiveBuffers, afterStats.PeakLiveBuffers);
            Assert.Equal(beforeStats.DriverAllocations, afterStats.DriverAllocations);
            Assert.Equal(0u, Context.LastModPowNttBlockDim);
        }
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(256u)]
    [InlineData(96u)]
    public void ExecuteModPowNttMegaKernel_ValidWarpMultipleBlockDim_MatchesBigIntegerReference(uint blockDim)
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 64;
        const int TargetBits = 96 * NumWords + 512;
        var (gpuBase, gpuExp, gpuMod, gpuRecip, gpuResult, expected) =
            CreateModPowNttMegaKernelTestInputs(Context, NumWords, TargetBits, new BigInteger(65537));
        using (gpuBase)
        using (gpuExp)
        using (gpuMod)
        using (gpuRecip)
        using (gpuResult)
        {
            new KernelAdvancedMathHandler().ExecuteModPowNttMegaKernel(
                Context, gpuBase, gpuResult, gpuMod, gpuRecip, gpuExp,
                NumWords, TargetBits, gridDim: 1, blockDim: blockDim);

            Assert.Equal(expected, gpuResult.ToBigInteger());
            Assert.Equal(blockDim, Context.LastModPowNttBlockDim);
        }
    }

    // When m > 65536 (n = 6600 -> m = 131072), TransposeTileBatches requires blockDim >= 256; warp multiples below
    // 256 must throw ArgumentOutOfRangeException before any NttWorkspaceBuffers or twiddle allocation.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:278
    //         `ThrowIfStridedColumnBlockDimTooSmall(blockDim, transformLength);` -> removed
    // input: n = 6600, targetBits = 96 * 6600 + 512, exp = 3, blockDim = 32, 96, 224
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    [InlineData(32u)]
    [InlineData(96u)]
    [InlineData(224u)]
    public void ExecuteModPowNttMegaKernel_AboveStridedColumnLimitWithSmallBlockDim_ThrowsArgumentOutOfRangeException(
        uint blockDim)
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 6600;
        const int TargetBits = 96 * NumWords + 512;
        using var workspace = new GpuWorkspace(NumWords * 16, GpuExecutionEngine.GSharpAot);
        var (gpuBase, gpuExp, gpuMod, gpuRecip, gpuResult, _) =
            CreateModPowNttMegaKernelTestInputs(workspace, NumWords, TargetBits, new BigInteger(3));
        using (gpuBase)
        using (gpuExp)
        using (gpuMod)
        using (gpuRecip)
        using (gpuResult)
        {
            var beforeStats = workspace.GetMemoryStatistics();
            var handler = new KernelAdvancedMathHandler();
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                handler.ExecuteModPowNttMegaKernel(
                    workspace, gpuBase, gpuResult, gpuMod, gpuRecip, gpuExp,
                    NumWords, TargetBits, gridDim: 1, blockDim: blockDim));

            Assert.Equal("blockDim", exception.ParamName);
            var afterStats = workspace.GetMemoryStatistics();
            Assert.Equal(beforeStats.PeakLiveBuffers, afterStats.PeakLiveBuffers);
            Assert.Equal(beforeStats.DriverAllocations, afterStats.DriverAllocations);
            Assert.Equal(0u, workspace.LastModPowNttBlockDim);
        }
    }

    // Boundary check at m = 131072 > 65536: blockDim = 256 meets TransposeTileBatches' 8-warp tile minimum and
    // matches BigInteger.ModPow.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs:567
    //         `blockDim < StridedColumnMinBlockDim` -> `blockDim <= StridedColumnMinBlockDim` (256u throws)
    // input: n = 6600, targetBits = 96 * 6600 + 512, exp = 3, blockDim = 256
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void ExecuteModPowNttMegaKernel_AboveStridedColumnLimitWithMinBlockDim_MatchesBigIntegerReference()
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 6600;
        const int TargetBits = 96 * NumWords + 512;
        using var workspace = new GpuWorkspace(NumWords * 16, GpuExecutionEngine.GSharpAot);
        var (gpuBase, gpuExp, gpuMod, gpuRecip, gpuResult, expected) =
            CreateModPowNttMegaKernelTestInputs(workspace, NumWords, TargetBits, new BigInteger(3));
        using (gpuBase)
        using (gpuExp)
        using (gpuMod)
        using (gpuRecip)
        using (gpuResult)
        {
            const uint BlockDim = 256u;
            new KernelAdvancedMathHandler().ExecuteModPowNttMegaKernel(
                workspace, gpuBase, gpuResult, gpuMod, gpuRecip, gpuExp,
                NumWords, TargetBits, gridDim: 1, blockDim: BlockDim);

            Assert.True(expected == gpuResult.ToBigInteger(), "n=6600 blockDim=256 ModPow mismatch");
            Assert.Equal(BlockDim, workspace.LastModPowNttBlockDim);
        }
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(2048, 64)]
    [InlineData(2048, 2048)]
    [InlineData(4096, 64)]
    [InlineData(4096, 4096)]
    [InlineData(8192, 64)]
    public void ModPow_ZeroSyncGraphWidths_MatchesBigIntegerReference(int modulusBits, int expBits)
    {
        CudaCapability.EnsureGpuAvailable();

        var numWords = modulusBits / 32;
        var expWords = (expBits + 31) / 32;
        using var workspace = new GpuWorkspace(Math.Max(4096, numWords * 16), GpuExecutionEngine.GSharpAot);

        var seed = 2346 + modulusBits * 17 + expBits;
        var random = new Random(seed);
        var modValue = GenerateFullWidthBigInteger(random, modulusBits) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, modulusBits) % modValue;
        var expValue = GenerateFullWidthBigInteger(random, expBits);
        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, numWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(workspace, expValue, expWords);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, numWords);
        using var gpuResult = new GpuBigUInt(workspace, numWords);

        var launchesBefore = workspace.ModPowGraphLaunches;
        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);
        var actual = gpuResult.ToBigInteger();

        // Result must match host BigInteger.ModPow and use CUDA Graph launches for wide moduli.
        // mutant: ModPowZeroSyncController.cs:379 `squareGraph.Launch();`
        // -> `squareGraph.Launch(); squareGraph.Launch();`
        Assert.True(expected == actual, $"seed {seed}: expected {expected}, actual {actual}");
        Assert.True(workspace.ModPowGraphLaunches - launchesBefore > 0);
        workspace.ThrowIfDeviceErrors();
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(64, true, 128)]
    [InlineData(63, false, 65)]
    public void ModPow_AllOnesAndPowerOfTwoExponents_MatchesReferenceAndLaunchCount(
        int bitCount,
        bool allOnes,
        int expectedSteps)
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 64;
        using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

        var seed = 2347 + bitCount;
        var random = new Random(seed);
        var modValue = GenerateFullWidthBigInteger(random, NumWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, NumWords * 32) % modValue;
        var expValue = allOnes ? (BigInteger.One << bitCount) - 1 : BigInteger.One << bitCount;
        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, NumWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(workspace, expValue, 2);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, NumWords);
        using var gpuResult = new GpuBigUInt(workspace, NumWords);

        var launchesBefore = workspace.ModPowGraphLaunches;
        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

        // Every multiply and square step must execute; exact graph launches equal steps - 1 (after 1 warm-up step).
        // mutant: ModPowZeroSyncController.cs:368 `multiplyGraph.Launch();` -> removed
        // mutant: ModPowZeroSyncController.cs:380 `workspace.CountModPowGraphLaunch();` -> removed
        Assert.True(expected == gpuResult.ToBigInteger(), $"seed {seed} mismatch");
        Assert.Equal(expectedSteps - 1L, workspace.ModPowGraphLaunches - launchesBefore);
        workspace.ThrowIfDeviceErrors();
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void ModPow_SparseExponentWithZeroWordsInPaddedBuffer_BreaksAtTopNonZeroWord()
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 64;
        using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

        const int Seed = 2349;
        var random = new Random(Seed);
        var modValue = GenerateFullWidthBigInteger(random, NumWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, NumWords * 32) % modValue;
        var expValue = (BigInteger.One << 200) + 1;
        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, NumWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(workspace, expValue, NumWords);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, NumWords);
        using var gpuResult = new GpuBigUInt(workspace, NumWords);

        // Word 0 has 1 mul + 32 sq = 33; words 1..5 have 5 * 32 = 160 sq; word 6 has 32 sq + 1 mul = 33; total = 226.
        const long ExpectedLaunches = 225L;
        var launchesBefore = workspace.ModPowGraphLaunches;
        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

        // Outer allZero break must stop at word 7 rather than launching 57 extra words of 32 squarings each.
        // mutant: ModPowZeroSyncController.cs:214 `break;` -> removed
        Assert.True(expected == gpuResult.ToBigInteger(), $"seed {Seed} mismatch");
        Assert.Equal(ExpectedLaunches, workspace.ModPowGraphLaunches - launchesBefore);
        workspace.ThrowIfDeviceErrors();
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0u, 0L)]
    [InlineData(1u, 0L)]
    [InlineData(2u, 0L)]
    [InlineData(3u, 0L)]
    [InlineData(32u, 0L)]
    [InlineData(64u, 7L)]
    public void ModPow_FewStepsAndMinimumGraphStepsBoundary_UsesExpectedPathAndMatchesReference(
        uint expWord,
        long expectedLaunches)
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 64;
        using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

        var seed = 2350 + (int)expWord;
        var random = new Random(seed);
        var modValue = GenerateFullWidthBigInteger(random, NumWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, NumWords * 32) % modValue;
        var expValue = new BigInteger(expWord);
        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, NumWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(workspace, expValue, 1);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, NumWords);
        using var gpuResult = new GpuBigUInt(workspace, NumWords);

        var launchesBefore = workspace.ModPowGraphLaunches;
        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

        // Below MinimumGraphSteps (8) the plain loop must run with 0 graph launches; at 8 steps, graph playback runs.
        // mutant: ModPowZeroSyncController.cs:11 `internal const int MinimumGraphSteps = 8;` -> `... = 0;`
        Assert.True(expected == gpuResult.ToBigInteger(), $"seed {seed}, exp {expWord} mismatch");
        Assert.Equal(expectedLaunches, workspace.ModPowGraphLaunches - launchesBefore);
        workspace.ThrowIfDeviceErrors();
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void ExecuteLadder_InsideUserStreamCapture_SkipsInternalCaptureAndReplaysCorrectly()
    {
        CudaCapability.EnsureGpuAvailable();

        const int NumWords = 64;
        using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

        const int Seed = 2351;
        var random = new Random(Seed);
        var modValue = GenerateFullWidthBigInteger(random, NumWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, NumWords * 32) % modValue;
        var expValue = (BigInteger.One << 64) - 1;
        var expected = BigInteger.ModPow(baseValue, expValue, modValue);

        using var gpuBase = GpuBigUInt.FromBigInteger(workspace, baseValue, NumWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(workspace, expValue, 2);
        using var gpuMod = GpuBigUInt.FromBigInteger(workspace, modValue, NumWords);
        using var gpuResult = new GpuBigUInt(workspace, NumWords);

        // Warm up pooled device buffers and loaded modules before stream capture.
        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);
        Assert.True(expected == gpuResult.ToBigInteger(), $"seed {Seed} warmup mismatch");

        using var buffers = ModPowZeroSyncController.PrepareLadder(gpuBase, gpuMod);
        uint[] hostExp = [uint.MaxValue, uint.MaxValue];

        var launchesBefore = workspace.ModPowGraphLaunches;
        var graph = IntPtr.Zero;
        var graphExec = IntPtr.Zero;
        var manager = (DefaultGpuMemoryManager)workspace.MemoryManager;
        var deferral = manager.BeginDeferral();
        CapturedResources? captured = null;
        try
        {
            workspace.Provider.BeginCapture(workspace.Stream);
            // CaptureChecks.IsCapturing(workspace) must skip nested graph capture inside a user-started capture.
            // mutant: ModPowZeroSyncController.cs:84 `CaptureChecks.IsCapturing(buffers.Workspace) ||` -> removed
            ModPowZeroSyncController.ExecuteLadder(buffers, hostExp, 2);
            graph = workspace.Provider.EndCapture(workspace.Stream);
            captured = manager.EndDeferral(deferral);
            graphExec = workspace.Provider.InstantiateGraph(graph);
            workspace.Provider.LaunchGraph(graphExec, workspace.Stream);
            workspace.Provider.SynchronizeStream(workspace.Stream);

            gpuResult.CopyFrom(buffers.TempResult, numWords: NumWords);
            Assert.True(expected == gpuResult.ToBigInteger(), $"seed {Seed} captured replay mismatch");
            Assert.Equal(0L, workspace.ModPowGraphLaunches - launchesBefore);
            workspace.ThrowIfDeviceErrors();
        }
        finally
        {
            if (captured is null)
            {
                try
                {
                    var aborted = workspace.Provider.EndCapture(workspace.Stream);
                    if (aborted != IntPtr.Zero)
                    {
                        workspace.Provider.DestroyGraph(aborted, IntPtr.Zero);
                    }
                }
                catch (CudaException)
                {
                    // Stream capture may already be invalidated when nested capture throws.
                }

                captured = manager.EndDeferral(deferral);
            }

            if (graph != IntPtr.Zero || graphExec != IntPtr.Zero)
            {
                workspace.Provider.DestroyGraph(graph, graphExec);
            }

            manager.ReturnDeferred(captured.Value.Buffers);
        }
    }

    private static (
        GpuBigUInt Base,
        GpuBigUInt Exp,
        GpuBigUInt Mod,
        GpuBigUInt Reciprocal,
        GpuBigUInt Result,
        BigInteger Expected)
        CreateModPowNttMegaKernelTestInputs(
            GpuWorkspace workspace, int numWords, int targetBits, BigInteger expValue)
    {
        var random = new Random(606 + numWords);
        var modValue = GenerateFullWidthBigInteger(random, numWords * 32) | BigInteger.One;
        var baseValue = GenerateFullWidthBigInteger(random, numWords * 32) % modValue;
        var expected = BigInteger.ModPow(baseValue, expValue, modValue);
        var reciprocalValue = (BigInteger.One << targetBits) / modValue;
        var targetWords = (targetBits + 31) / 32;

        return (
            GpuBigUInt.FromBigInteger(workspace, baseValue, numWords),
            GpuBigUInt.FromBigInteger(workspace, expValue, 1),
            GpuBigUInt.FromBigInteger(workspace, modValue, numWords),
            GpuBigUInt.FromBigInteger(workspace, reciprocalValue, targetWords),
            new GpuBigUInt(workspace, numWords),
            expected);
    }
}
