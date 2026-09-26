using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

namespace Kassyi.GSharp.Stdlib.Math.Tests.BigInt;

[Trait("Category", "GpuExecution")]
public class GBI26_GpuUIntBigIntegerCoreTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuContext _context;

    public GBI26_GpuUIntBigIntegerCoreTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }

    [SkippableTheory]
    [Trait("FeatureId", "GBI-26")]
    [Trait("Category", "HappyPath")]
    [InlineData("0", 1)]
    [InlineData("123456789012345678901234567890", 4)]
    [InlineData("-123456789012345678901234567890", 4)] // Should take absolute value
    public void Test_GBI26_FromBigInteger_ToBigInteger_RoundTripsCorrectly(string valueStr, int minWords)
    {
        CudaCapability.EnsureGpuAvailable();
        var original = BigInteger.Parse(valueStr);
        using var gpuBigInt = GpuUIntBigInteger.FromBigInteger(_context, original, minWords);
        
        Assert.True(gpuBigInt.NumWords >= minWords);

        var roundTripped = gpuBigInt.ToBigInteger();
        Assert.Equal(BigInteger.Abs(original), roundTripped);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-26")]
    [Trait("Category", "EdgeCase")]
    public void Test_GBI26_PadTo_LargerSize_ZeroPadsCorrectly()
    {
        CudaCapability.EnsureGpuAvailable();
        var original = new BigInteger(0xDEADBEEF);
        using var gpuBigInt = GpuUIntBigInteger.FromBigInteger(_context, original, 1);
        
        using var padded = gpuBigInt.PadTo(4);
        Assert.Equal(4, padded.NumWords);
        
        var hostData = padded.ToHostArray();
        Assert.Equal(0xDEADBEEF, hostData[0]);
        Assert.Equal(0u, hostData[1]);
        Assert.Equal(0u, hostData[2]);
        Assert.Equal(0u, hostData[3]);
        
        Assert.Equal(original, padded.ToBigInteger());
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-26")]
    public void Test_GBI26_PadTo_SmallerSize_ReturnsTruncatedValue()
    {
        CudaCapability.EnsureGpuAvailable();
        var originalData = new uint[] { 1, 2, 3, 4, 5 };
        using var original = new GpuUIntBigInteger(_context, originalData);

        using var padded = original.PadTo(3);

        Assert.Equal(3, padded.NumWords);
        
        var resultData = new uint[3];
        _context.Provider.CopyDeviceToHost(resultData, padded.DevicePointer, 3, _context.Stream);
        
        Assert.Equal(3, resultData.Length);
        Assert.Equal(1u, resultData[0]);
        Assert.Equal(2u, resultData[1]);
        Assert.Equal(3u, resultData[2]);
    }
    [SkippableFact]
    [Trait("FeatureId", "GBI-26")]
    [Trait("Category", "HappyPath")]
    public void Test_GBI26_Addition_EvaluatesCorrectly()
    {
        CudaCapability.EnsureGpuAvailable();
        var originalA = BigInteger.Parse("1234567890123456789012345678901234567890");
        var originalB = BigInteger.Parse("9876543210987654321098765432109876543210");
        
        using var a = GpuUIntBigInteger.FromBigInteger(_context, originalA);
        using var b = GpuUIntBigInteger.FromBigInteger(_context, originalB);

        var expr = a + b;
        using var result = expr.Node.Evaluate(_context, null);

        Assert.Equal(originalA + originalB, result.ToBigInteger());
    }
}
