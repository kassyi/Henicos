using System.Security.Cryptography;

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

    private static BigInteger GenerateRandomBigInteger(int bits)
    {
        var bytes = new byte[(bits + 7) / 8];
        RandomNumberGenerator.Fill(bytes);

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
        var baseValue = GenerateRandomBigInteger(bitLength);
        var expValue = GenerateRandomBigInteger(expBitLength);
        var modValue = GenerateRandomBigInteger(bitLength);

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
