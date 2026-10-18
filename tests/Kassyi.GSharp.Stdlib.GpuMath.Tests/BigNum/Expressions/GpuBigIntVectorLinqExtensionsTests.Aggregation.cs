
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

public partial class GpuBigIntVectorLinqExtensionsTests
{
    public static IEnumerable<object[]> AggregationData()
    {
        // Happy Path
        yield return new object[] { TestScenario.HappyPath, new BigInteger[] { 10, -5, 15, 100 }, new BigInteger(120), new BigInteger(100), new BigInteger(-5), 4 };
        // Boundary (Zeros)
        yield return new object[] { TestScenario.Boundary, new BigInteger[] { 0, 0, 0, 0 }, new BigInteger(0), new BigInteger(0), new BigInteger(0), 4 };
        // Edge (Large Grid, mixed signs)
        var largeArray = new BigInteger[2048];
        BigInteger sum = 0;
        BigInteger max = -10000;
        BigInteger min = 10000;
        for (int i = 0; i < 2048; i++)
        {
            largeArray[i] = (i % 2 == 0) ? i : -i;
            sum += largeArray[i];
            if (largeArray[i] > max) max = largeArray[i];
            if (largeArray[i] < min) min = largeArray[i];
        }
        yield return new object[] { TestScenario.Boundary, largeArray, sum, max, min, 2048 };
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(AggregationData))]
    public void Sum_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger expectedSum, BigInteger expectedMax, BigInteger expectedMin, int expectedCount)
    {
        using var array = CreateArray(input);
        var sumExpr = array.AsVectorizedEnumerable().Sum();
        using var result = sumExpr.Evaluate(_context);
        Assert.Equal(expectedSum, ToBigInt(result));
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(AggregationData))]
    public void Max_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger expectedSum, BigInteger expectedMax, BigInteger expectedMin, int expectedCount)
    {
        using var array = CreateArray(input);
        var maxExpr = array.AsVectorizedEnumerable().Max();
        using var result = maxExpr.Evaluate(_context);
        Assert.Equal(expectedMax, ToBigInt(result));
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(AggregationData))]
    public void Min_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger expectedSum, BigInteger expectedMax, BigInteger expectedMin, int expectedCount)
    {
        using var array = CreateArray(input);
        var minExpr = array.AsVectorizedEnumerable().Min();
        using var result = minExpr.Evaluate(_context);
        Assert.Equal(expectedMin, ToBigInt(result));
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(AggregationData))]
    public void Count_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger expectedSum, BigInteger expectedMax, BigInteger expectedMin, int expectedCount)
    {
        using var array = CreateArray(input);
        var countExpr = array.AsVectorizedEnumerable().Count();
        using var result = countExpr.Evaluate(_context);
        Assert.Equal(expectedCount, (int)ToBigInt(result));
    }
}
