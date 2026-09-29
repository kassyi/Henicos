
namespace Kassyi.GSharp.Stdlib.Math.Tests.BigInt;

[Trait("Category", "GpuExecution")]
public class GBI29_GpuBigIntMathPow10Tests
{
    private static GpuExecutionEngine CreateTestEngine() => GpuExecutionEngine.GSharpAot;

    [Trait("FeatureId", "GBI-29")]
    [Trait("Category", "HappyPath")]
    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Test_GBI29_Pow10_ReturnsCorrectResult(int exponent)
    {
        CudaCapability.EnsureGpuAvailable();
        var engine = CreateTestEngine();
        using var context = new GpuWorkspace(1024, engine);

        using var resultGpu = GpuBigIntMath.Pow10(context, exponent);
        var expected = BigInteger.Pow(10, exponent);
        Assert.Equal(expected, resultGpu.ToBigInteger());
    }
    
    [Trait("FeatureId", "GBI-29")]
    [Trait("Category", "SadPath")]
    [SkippableTheory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(50_000_001)]
    [InlineData(int.MaxValue)]
    public void Test_GBI29_Pow10_InvalidExponent_ThrowsArgumentOutOfRangeException(int exponent)
    {
        CudaCapability.EnsureGpuAvailable();
        var engine = CreateTestEngine();
        using var context = new GpuWorkspace(1024, engine);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            GpuBigIntMath.Pow10(context, exponent);
        });
    }
}
