namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class ModPowPolyAlgorithmTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public ModPowPolyAlgorithmTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }

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
    public void Test_ModPow_CrossOver_Verification(int bitLength)
    {
        CudaCapability.EnsureGpuAvailable();

        // Arrange: Generate random BigIntegers.
        // For >1024-bit host-loop crossover cases, use a 64-bit (2-word) exponent to verify full-width
        // 2048/4096-bit multiplications and Barrett reductions across word boundaries without thousands of host-loop iterations.
        int expBitLength = bitLength <= _context.PolyAlgorithmThreshold ? bitLength / 2 : 64;
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

        using var gpuBase = GpuBigUInt.FromBigInteger(_context, baseValue, numWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(_context, expValue, numWords);
        using var gpuMod = GpuBigUInt.FromBigInteger(_context, modValue, numWords);
        using var gpuResult = new GpuBigUInt(_context, numWords);

        // Act
        GpuBigIntDivRem.ModPow(gpuBase, gpuExp, gpuMod, gpuResult);

        // Assert
        var actual = gpuResult.ToBigInteger();
        Assert.Equal(expected, actual);
    }

    // ModPowNttHostController launches ModPowNttMegaKernel with one block of 256 threads, and the kernel resolves the
    // carries of each product in chunks of one block (BlockDimension.X), writing one block carry per chunk into
    // BlockCarries. Its largest product (the Barrett step) has 2n + (3n + 16) = 5n + 16 words: n = 99 gives 511 words
    // (2 chunks), n = 100 gives 516 words (3 chunks, one more than an allocation sized for 1024-word chunks holds) and
    // n = 200 gives 1016 words (4 chunks). The product A * R of the Barrett step is below 2^(32 * (4n + 16)), so its top
    // chunks are zero: only n = 200 carries a non-zero value across the second chunk boundary.
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Providers/Handlers/KernelAdvancedMathHandler.cs
    //   NttWorkspaceBuffers `ChunkSize = carryChunkSize;` -> `ChunkSize = 1024;` (the #606 allocation)
    //   input: numWords 100 and 200 (ThrowIfBlockCarriesTooSmall throws before the launch)
    // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Kernels/GpuBigIntDivRemKernels.ModPow.cu.cs:66
    //   `int numBlocks = (numWordsResult + chunkSize - 1) / chunkSize;` -> `int numBlocks = 1;`
    //   input: numWords 200 (the carry out of the second chunk is lost and the result differs from BigInteger.ModPow)
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(99)]
    [InlineData(100)]
    [InlineData(200)]
    public void Test_ModPow_NttPath_CarryChunksOfLaunchBlock(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();

        using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
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
    public void Test_ModPow_EdgeCases()
    {
        CudaCapability.EnsureGpuAvailable();
        
        // Arrange
        var modValue = BigInteger.Parse("123456789012345678901234567890123456789012345678901234567891"); // odd
        var numWords = 10;
        
        using var gpuMod = GpuBigUInt.FromBigInteger(_context, modValue, numWords);
        using var gpuResult = new GpuBigUInt(_context, numWords);

        // 1. Base 0
        using var gpuBase0 = GpuBigUInt.FromBigInteger(_context, BigInteger.Zero, numWords);
        using var gpuExp = GpuBigUInt.FromBigInteger(_context, new BigInteger(5), numWords);
        GpuBigIntDivRem.ModPow(gpuBase0, gpuExp, gpuMod, gpuResult);
        Assert.Equal(BigInteger.ModPow(BigInteger.Zero, 5, modValue), gpuResult.ToBigInteger());

        // 2. Exponent 0
        using var gpuBase5 = GpuBigUInt.FromBigInteger(_context, new BigInteger(5), numWords);
        using var gpuExp0 = GpuBigUInt.FromBigInteger(_context, BigInteger.Zero, numWords);
        GpuBigIntDivRem.ModPow(gpuBase5, gpuExp0, gpuMod, gpuResult);
        Assert.Equal(BigInteger.ModPow(5, BigInteger.Zero, modValue), gpuResult.ToBigInteger());
        
        // 3. Base 1
        using var gpuBase1 = GpuBigUInt.FromBigInteger(_context, BigInteger.One, numWords);
        GpuBigIntDivRem.ModPow(gpuBase1, gpuExp, gpuMod, gpuResult);
        Assert.Equal(BigInteger.ModPow(BigInteger.One, 5, modValue), gpuResult.ToBigInteger());

        // 4. Modulus 0 (DivideByZeroException)
        using var gpuMod0 = GpuBigUInt.FromBigInteger(_context, BigInteger.Zero, numWords);
        Assert.Throws<DivideByZeroException>(() => GpuBigIntDivRem.ModPow(gpuBase5, gpuExp, gpuMod0, gpuResult));

        // 5. Size Mismatch (ArgumentException)
        using var gpuMismatch = new GpuBigUInt(_context, numWords + 1);
        Assert.Throws<ArgumentException>(() => GpuBigIntDivRem.ModPow(gpuMismatch, gpuExp, gpuMod, gpuResult));
    }
}
