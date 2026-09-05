using System.Numerics;
using Kassyi.GSharp.Stdlib.Math.BigInt;

namespace Kassyi.GSharp.Stdlib.Math.Tests.BigInt;

public class GBI29_GpuBigIntMathPow10Tests
{
    private static GpuExecutionEngine CreateTestEngine() => GpuExecutionEngine.GSharpJit;

    [Trait("FeatureId", "GBI-29")]
    [Trait("Category", "HappyPath")]
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Pow10_ReturnsCorrectResult(int exponent)
    {
        var engine = CreateTestEngine();
        using var context = new GpuContext(1024, engine);

        var resultGpu = GpuBigInteger.FromBigInteger(context, 1);
        var currentGpu = GpuBigInteger.FromBigInteger(context, 10);
        
        var resultCpu = new BigInteger(1);
        var currentCpu = new BigInteger(10);

        int step = 1;
        int currentExponent = exponent;

        while (currentExponent > 0)
        {
            if ((currentExponent & 1) == 1)
            {
                var newResultWords = resultGpu.NumWords + currentGpu.NumWords;
                var nextResult = new GpuBigInteger(context, newResultWords);
                GpuBigIntMath.Mul(resultGpu, currentGpu, nextResult);

                var actualResultWords = (GpuBigIntDivRem.GetActualBits(nextResult) + 31) / 32;
                if (actualResultWords == 0) actualResultWords = 1;
                var trimmedResult = nextResult.PadTo(actualResultWords);
                
                resultCpu *= currentCpu;
                var gpuVal = trimmedResult.ToBigInteger();
                Assert.True(gpuVal == resultCpu, $"[FAIL] Step {step} (Mul into Result): GPU={gpuVal}, CPU={resultCpu}");
                
                nextResult.Dispose();
                resultGpu.Dispose();
                resultGpu = trimmedResult;
            }

            currentExponent >>= 1;

            if (currentExponent > 0)
            {
                var newCurrentWords = currentGpu.NumWords * 2;
                var nextCurrent = new GpuBigInteger(context, newCurrentWords);
                GpuBigIntMath.Mul(currentGpu, currentGpu, nextCurrent);

                var actualCurrentWords = (GpuBigIntDivRem.GetActualBits(nextCurrent) + 31) / 32;
                if (actualCurrentWords == 0) actualCurrentWords = 1;
                var trimmedCurrent = nextCurrent.PadTo(actualCurrentWords);
                
                currentCpu *= currentCpu;
                var gpuVal = trimmedCurrent.ToBigInteger();
                Assert.True(gpuVal == currentCpu, $"[FAIL] Step {step} (Square Current): GPU={gpuVal}, CPU={currentCpu}");
                
                nextCurrent.Dispose();
                currentGpu.Dispose();
                currentGpu = trimmedCurrent;
            }
            step++;
        }

        var expected = BigInteger.Pow(10, exponent);
        Assert.Equal(expected, resultGpu.ToBigInteger());
    }
    
    [Trait("FeatureId", "GBI-29")]
    [Trait("Category", "SadPath")]
    [Fact]
    public void Pow10_NegativeExponent_ThrowsArgumentOutOfRangeException()
    {
        var engine = CreateTestEngine();
        using var context = new GpuContext(1024, engine);

        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
        {
            GpuBigIntMath.Pow10(context, -1);
        });
    }
}
