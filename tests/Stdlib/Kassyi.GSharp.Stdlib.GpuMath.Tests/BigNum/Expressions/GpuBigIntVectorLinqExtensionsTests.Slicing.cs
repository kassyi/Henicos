
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

public partial class GpuBigIntVectorLinqExtensionsTests
{
    public static IEnumerable<object[]> SlicingData()
    {
        // length 6
        yield return new object[] { TestScenario.HappyPath, new BigInteger[] { 10, 20, 30, 40, 50, 60 }, 2, 3, new BigInteger[] { 10, 20 }, new BigInteger[] { 40, 50, 60 } };
        yield return new object[] { TestScenario.Boundary, new BigInteger[] { 10, 20, 30, 40, 50, 60 }, 0, 6, new BigInteger[0], new BigInteger[0] };
        yield return new object[] { TestScenario.Boundary, new BigInteger[] { 10, 20, 30, 40, 50, 60 }, 10, 10, new BigInteger[] { 10, 20, 30, 40, 50, 60 }, new BigInteger[0] };
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(SlicingData))]
    public void Take_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, int takeCount, int skipCount, BigInteger[] expectedTake, BigInteger[] expectedSkip)
    {
        using var array = CreateArray(input);
        var expr = array.AsVectorizedEnumerable().Take(takeCount);
        var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        Assert.Equal(expectedTake.Length, hostResult.Length);
        for(int i=0; i<expectedTake.Length; i++) Assert.Equal(expectedTake[i], hostResult[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(SlicingData))]
    public void Skip_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, int takeCount, int skipCount, BigInteger[] expectedTake, BigInteger[] expectedSkip)
    {
        using var array = CreateArray(input);
        var expr = array.AsVectorizedEnumerable().Skip(skipCount);
        var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        Assert.Equal(expectedSkip.Length, hostResult.Length);
        for(int i=0; i<expectedSkip.Length; i++) Assert.Equal(expectedSkip[i], hostResult[i]);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void TakeAndSkip_Combined_EvaluatesCorrectly()
    {
        using var array = CreateArray(new BigInteger[] { 10, 20, 30, 40, 50, 60 });
        // Skip 2 (30, 40, 50, 60) -> Take 2 (30, 40)
        var expr = array.AsVectorizedEnumerable().Skip(2).Take(2);
        var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        Assert.Equal(2, hostResult.Length);
        Assert.Equal(30, hostResult[0]);
        Assert.Equal(40, hostResult[1]);
    }
}
