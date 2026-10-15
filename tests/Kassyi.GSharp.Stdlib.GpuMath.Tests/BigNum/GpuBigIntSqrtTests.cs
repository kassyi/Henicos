using System.Numerics;


namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigIntSqrtTests
{
    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Sqrt_SmallValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        
        using var a = new GpuBigUInt(ctx, new uint[] { 10000, 0, 0, 0 });
        using var result = new GpuBigUInt(ctx, 4);
        
        GpuBigIntDivRem.Sqrt(a, result);
        
        Assert.Equal(100u, result.ToHostArray()[0]);
    }

    [SkippableFact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Sqrt_LargeValues()
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(512, GpuExecutionEngine.GSharpAot);
        
        var aArr = new uint[512];
        Array.Fill(aArr, 0xFFFFFFFFu);
        
        using var a = new GpuBigUInt(ctx, aArr);
        using var result = new GpuBigUInt(ctx, 512);
        
        GpuBigIntDivRem.Sqrt(a, result);
        
        Assert.True(result.NumWords > 0);
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(15u)]
    [InlineData(16u)]
    [InlineData(65535u * 65535u)]
    [InlineData(uint.MaxValue)]
    public void Test_Sqrt_SingleWordValueInWideBuffer_ReturnsFloorRoot(uint value)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(256, GpuExecutionEngine.GSharpAot);

        // Only word 0 carries data; the single-word fast path must still read exactly that word.
        var words = new uint[64];
        words[0] = value;
        using var a = new GpuBigUInt(ctx, words);
        using var result = new GpuBigUInt(ctx, 64);

        GpuBigIntDivRem.Sqrt(a, result);

        Assert.Equal(FloorSqrt(value), result.ToBigInteger());
    }

    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(128, 0)]
    [InlineData(128, -1)]
    [InlineData(200, 12345)]
    [InlineData(256, -1)]
    [InlineData(300, 1)]
    public void Test_Sqrt_MultiWordValue_ReturnsFloorRoot(int bits, int offset)
    {
        CudaCapability.EnsureGpuAvailable();
        using var ctx = new GpuWorkspace(512, GpuExecutionEngine.GSharpAot);

        var value = (BigInteger.One << bits) + offset;
        const int Words = 24;
        using var a = GpuBigUInt.FromBigInteger(ctx, value, Words);
        using var result = new GpuBigUInt(ctx, Words);

        GpuBigIntDivRem.Sqrt(a, result);

        Assert.Equal(FloorSqrt(value), result.ToBigInteger());
    }

    private static BigInteger FloorSqrt(BigInteger value)
    {
        if (value < 2)
        {
            return value;
        }

        var x = BigInteger.One << (int)((value.GetBitLength() + 1) / 2);
        while (true)
        {
            var next = (x + (value / x)) >> 1;
            if (next >= x)
            {
                return x;
            }

            x = next;
        }
    }
}
