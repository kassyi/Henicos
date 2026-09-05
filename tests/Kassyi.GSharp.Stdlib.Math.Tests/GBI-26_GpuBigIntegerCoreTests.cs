using System.Numerics;
using Kassyi.GSharp.Runtime.Jit;
using Kassyi.GSharp.Stdlib.Math.BigInt;

namespace Kassyi.GSharp.Stdlib.Math.Tests.BigInt;

public class GBI26_GpuBigIntegerCoreTests : IDisposable
{
    private readonly JitEngine.CudaContextScope _scope;
    private readonly GpuContext _context;

    public GBI26_GpuBigIntegerCoreTests()
    {
        _scope = new JitEngine.CudaContextScope();
        _context = new GpuContext(1024, GpuExecutionEngine.GSharpJit);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }

    [Theory]
    [Trait("FeatureId", "GBI-26")]
    [Trait("Category", "HappyPath")]
    [InlineData("0", 1)]
    [InlineData("123456789012345678901234567890", 4)]
    [InlineData("-123456789012345678901234567890", 4)] // Should take absolute value
    public void Test_GBI26_FromBigInteger_ToBigInteger_RoundTripsCorrectly(string valueStr, int minWords)
    {
        var original = BigInteger.Parse(valueStr);
        using var gpuBigInt = GpuBigInteger.FromBigInteger(_context, original, minWords);
        
        Assert.True(gpuBigInt.NumWords >= minWords);

        var roundTripped = gpuBigInt.ToBigInteger();
        Assert.Equal(BigInteger.Abs(original), roundTripped);
    }

    [Fact]
    [Trait("FeatureId", "GBI-26")]
    [Trait("Category", "EdgeCase")]
    public void Test_GBI26_PadTo_LargerSize_ZeroPadsCorrectly()
    {
        var original = new BigInteger(0xDEADBEEF);
        using var gpuBigInt = GpuBigInteger.FromBigInteger(_context, original, 1);
        
        using var padded = gpuBigInt.PadTo(4);
        Assert.Equal(4, padded.NumWords);
        
        var hostData = padded.ToHostArray();
        Assert.Equal(0xDEADBEEF, hostData[0]);
        Assert.Equal(0u, hostData[1]);
        Assert.Equal(0u, hostData[2]);
        Assert.Equal(0u, hostData[3]);
        
        Assert.Equal(original, padded.ToBigInteger());
    }

    [Fact]
    [Trait("FeatureId", "GBI-26")]
    public void Test_GBI26_PadTo_SmallerSize_ReturnsTruncatedValue()
    {
        var originalData = new uint[] { 1, 2, 3, 4, 5 };
        using var original = new GpuBigInteger(_context, originalData);

        using var padded = original.PadTo(3);

        Assert.Equal(3, padded.NumWords);
        
        var resultData = new uint[3];
        _context.Provider.CopyDeviceToHost(resultData, padded.DevicePointer, 3);
        
        Assert.Equal(3, resultData.Length);
        Assert.Equal(1u, resultData[0]);
        Assert.Equal(2u, resultData[1]);
        Assert.Equal(3u, resultData[2]);
    }
}
