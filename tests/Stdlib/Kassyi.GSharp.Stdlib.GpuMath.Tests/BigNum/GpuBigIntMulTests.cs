
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntMulTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Mul_SmallValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        
        using var a = new GpuBigUInt(ctx, new uint[] { 3, 0, 0, 0 });
        using var b = new GpuBigUInt(ctx, new uint[] { 4, 0, 0, 0 });
        
        using var result = new GpuBigUInt(ctx, 8);
        
        GpuBigIntMath.Mul(a, b, result);
        
        Assert.Equal(12u, result.ToHostArray()[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Mul_LargeValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
        
        var aArr = new uint[512];
        var bArr = new uint[512];
        Array.Fill(aArr, 0xFFFFFFFFu);
        Array.Fill(bArr, 0xFFFFFFFFu);
        
        using var a = new GpuBigUInt(ctx, aArr);
        using var b = new GpuBigUInt(ctx, bArr);
        
        using var result = new GpuBigUInt(ctx, 1024);
        
        GpuBigIntMath.Mul(a, b, result);
        
        Assert.True(result.NumWords > 0);
    }

    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(4, 4, 8)]
    [InlineData(16, 16, 32)]
    [InlineData(64, 64, 128)]
    [InlineData(128, 128, 256)]
    [InlineData(256, 256, 512)]
    [InlineData(512, 512, 1024)]
    public void MultiplyNtt_VariousSizes_MatchesBigIntegerReference(int wordsA, int wordsB, int wordsResult)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(wordsResult * 4, GpuExecutionEngine.GSharpAot);

        var aArr = new uint[wordsA];
        var bArr = new uint[wordsB];
        for (var i = 0; i < wordsA; i++)
        {
            aArr[i] = (uint)((i + 1) * 0x9E3779B9u + 0x12345678u);
        }

        for (var i = 0; i < wordsB; i++)
        {
            bArr[i] = (uint)((i + 1) * 0x85EBCA6Bu + 0x87654321u);
        }

        var expected = ToBigInteger(aArr) * ToBigInteger(bArr);

        using var a = new GpuBigUInt(ctx, aArr);
        using var b = new GpuBigUInt(ctx, bArr);
        using var result = new GpuBigUInt(ctx, wordsResult);

        GpuBigIntMath.MultiplyNtt(a, b, result);

        Assert.Equal(expected, result.ToBigInteger());
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void MultiplyNtt_AllMaxWords_PropagatesCarriesCorrectly()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(512, GpuExecutionEngine.GSharpAot);

        var aArr = new uint[64];
        var bArr = new uint[64];
        Array.Fill(aArr, 0xFFFFFFFFu);
        Array.Fill(bArr, 0xFFFFFFFFu);

        var expected = ToBigInteger(aArr) * ToBigInteger(bArr);

        using var a = new GpuBigUInt(ctx, aArr);
        using var b = new GpuBigUInt(ctx, bArr);
        using var result = new GpuBigUInt(ctx, 128);

        GpuBigIntMath.MultiplyNtt(a, b, result);

        Assert.Equal(expected, result.ToBigInteger());
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    public void MultiplyNtt_AsymmetricAndZero_EvaluatesCorrectly()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(512, GpuExecutionEngine.GSharpAot);

        var aArr = new uint[96];
        for (var i = 0; i < aArr.Length; i++)
        {
            aArr[i] = (uint)(0xCAFEBABEu + i * 31u);
        }

        var bArr = new uint[] { 0xDEADBEEFu, 0x01234567u, 0x89ABCDEFu };
        var expected = ToBigInteger(aArr) * ToBigInteger(bArr);

        using var a = new GpuBigUInt(ctx, aArr);
        using var b = new GpuBigUInt(ctx, bArr);
        using var result = new GpuBigUInt(ctx, 128);

        GpuBigIntMath.MultiplyNtt(a, b, result);
        Assert.Equal(expected, result.ToBigInteger());

        using var zero = new GpuBigUInt(ctx, new uint[16]);
        GpuBigIntMath.MultiplyNtt(a, zero, result);
        Assert.Equal(BigInteger.Zero, result.ToBigInteger());
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void LaunchMultiplyNttKernel_DirectDeviceExecution_MatchesExpected()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);

        var aArr = new uint[32];
        var bArr = new uint[32];
        for (var i = 0; i < 32; i++)
        {
            aArr[i] = (uint)(0x13579BDFu * (i + 1));
            bArr[i] = (uint)(0x2468ACE0u * (i + 3));
        }

        var expected = ToBigInteger(aArr) * ToBigInteger(bArr);

        using var a = new GpuBigUInt(ctx, aArr);
        using var b = new GpuBigUInt(ctx, bArr);
        using var result = new GpuBigUInt(ctx, 64);

        GpuBigIntMulKernels.LaunchMultiplyNttKernelPtr(
            result.DevicePointer, result.NumWords,
            a.DevicePointer, a.NumWords,
            b.DevicePointer, b.NumWords,
            gridDimX: 1, blockDimX: 256);

        Assert.Equal(expected, result.ToBigInteger());
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void MultiplyNtt_InsufficientResultCapacity_ThrowsArgumentException()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(64, GpuExecutionEngine.GSharpAot);

        using var a = new GpuBigUInt(ctx, new uint[8]);
        using var b = new GpuBigUInt(ctx, new uint[8]);
        using var resultTooSmall = new GpuBigUInt(ctx, 12);

        Assert.Throws<ArgumentException>(() => GpuBigIntMath.MultiplyNtt(a, b, resultTooSmall));
    }

    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(16)]
    [InlineData(128)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    public void Mul_SameOperandSquaring_SingleAndTwoDimensionalNtt_MatchesBigIntegerReference(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(numWords * 8, GpuExecutionEngine.GSharpAot);

        var aArr = new uint[numWords];
        for (var i = 0; i < numWords; i++)
        {
            aArr[i] = (uint)((i + 1) * 0x9E3779B9u + 0x12345678u);
        }

        var bigA = ToBigInteger(aArr);
        var expected = bigA * bigA;

        using var a = new GpuBigUInt(ctx, aArr);
        using var result = new GpuBigUInt(ctx, numWords * 2);

        GpuBigIntMath.Mul(a, a, result);

        Assert.Equal(expected, result.ToBigInteger());
    }

    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(512)]
    [InlineData(1024)]
    public void Mul_DistinctOperands_TwoDimensionalNtt_MatchesBigIntegerReference(int numWords)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(numWords * 8, GpuExecutionEngine.GSharpAot);

        var aArr = new uint[numWords];
        var bArr = new uint[numWords];
        for (var i = 0; i < numWords; i++)
        {
            aArr[i] = (uint)((i + 1) * 0x9E3779B9u + 0x12345678u);
            bArr[i] = (uint)((i + 1) * 0x85EBCA6Bu + 0x87654321u);
        }

        var expected = ToBigInteger(aArr) * ToBigInteger(bArr);

        using var a = new GpuBigUInt(ctx, aArr);
        using var b = new GpuBigUInt(ctx, bArr);
        using var result = new GpuBigUInt(ctx, numWords * 2);

        GpuBigIntMath.Mul(a, b, result);

        Assert.Equal(expected, result.ToBigInteger());
    }

    private static BigInteger ToBigInteger(uint[] words)
    {
        var bytes = new byte[words.Length * sizeof(uint)];
        Buffer.BlockCopy(words, 0, bytes, 0, bytes.Length);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }
}

