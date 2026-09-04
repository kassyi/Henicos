using System;
using System.Numerics;
using Kassyi.GSharp.Runtime.Jit;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Xunit;

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
    public void FromBigInteger_ToBigInteger_RoundTripsCorrectly(string valueStr, int minWords)
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
    public void PadTo_LargerSize_ZeroPadsCorrectly()
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
    [Trait("Category", "SadPath")]
    public void PadTo_SmallerSize_ThrowsArgumentOutOfRangeException()
    {
        using var gpuBigInt = new GpuBigInteger(_context, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => gpuBigInt.PadTo(2));
    }
}
