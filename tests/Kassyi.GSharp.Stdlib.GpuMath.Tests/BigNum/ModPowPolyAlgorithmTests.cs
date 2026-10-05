using System.Numerics;
using System.Security.Cryptography;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[Trait("Category", "GpuExecution")]
public class ModPowPolyAlgorithmTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public ModPowPolyAlgorithmTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
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
    [Trait("Category", "HappyPath")]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)] // Un-disabled to test extreme PCIe sync overhead resolution
    [InlineData(2048)]
    [InlineData(4096)] // Un-disabled to test extreme PCIe sync overhead resolution
    public void Test_ModPow_CrossOver_Verification(int bitLength)
    {
        CudaCapability.EnsureGpuAvailable();
        
        // Arrange: Generate random BigIntegers
        var baseValue = GenerateRandomBigInteger(bitLength);
        var expValue = GenerateRandomBigInteger(bitLength / 2); // Exponent is often smaller, but this is fine
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
    [Trait("Category", "EdgeCase")]
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
    }
}
