
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigFloatLinqAndVectorTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _context;

    public GpuBigFloatLinqAndVectorTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _context = new GpuWorkspace(1024 * 1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _context.Dispose();
        _scope.Dispose();
    }

    private GpuBigFloat CreateFloat(long value, int exponent = 0, int numWords = 4)
    {
        var isNeg = value < 0;
        var mag = (ulong)(isNeg ? -value : value);
        var words = new uint[numWords];
        words[0] = (uint)(mag & 0xFFFFFFFFu);
        if (numWords > 1)
        {
            words[1] = (uint)(mag >> 32);
        }

        var sig = new GpuBigUInt(_context, words);
        return new GpuBigFloat(sig, exponent, isNeg);
    }

    private static double ToDouble(GpuBigFloat value)
    {
        var mag = (double)value.UFloat.Significand.ToBigInteger() * Math.Pow(2.0, value.UFloat.Exponent);
        return value.IsNegative ? -mag : mag;
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigFloat_ScalarOperators_And_Comparisons_WorkCorrectly()
    {
        using var a = CreateFloat(20);
        using var b = CreateFloat(4);
        using var c = CreateFloat(-5);
        using var aScaled = CreateFloat(5, exponent: 2); // 5 * 2^2 = 20

        using GpuBigFloat sum = a + b;
        using GpuBigFloat diff = a - b;
        using GpuBigFloat prod = a * b;
        using GpuBigFloat quot = a / b;
        using GpuBigFloat negQuot = a / c;

        Assert.Equal(24.0, ToDouble(sum), precision: 8);
        Assert.Equal(16.0, ToDouble(diff), precision: 8);
        Assert.Equal(80.0, ToDouble(prod), precision: 8);
        Assert.Equal(5.0, ToDouble(quot), precision: 8);
        Assert.Equal(-4.0, ToDouble(negQuot), precision: 8);

        Assert.True(a == aScaled);
        Assert.False(a != aScaled);
        Assert.True(a > b);
        Assert.True(b < a);
        Assert.True(c < b);
        Assert.True(a >= aScaled);
        Assert.True(a <= aScaled);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void StackEnumerable_SelectArithmetic_And_Reductions_CalculateCorrectResults()
    {
        using var f0 = CreateFloat(10);
        using var f1 = CreateFloat(20);
        using var f2 = CreateFloat(30);
        using var f3 = CreateFloat(40);
        using var scalar = CreateFloat(5);

        var array = new[] { f0, f1, f2, f3 };

        using var sum = array.AsStackEnumerable().Sum();
        using var max = array.AsStackEnumerable().Max();
        using var min = array.AsStackEnumerable().Min();
        using var avg = array.AsStackEnumerable().Average();

        Assert.Equal(100.0, ToDouble(sum), precision: 8);
        Assert.Equal(40.0, ToDouble(max), precision: 8);
        Assert.Equal(10.0, ToDouble(min), precision: 8);
        Assert.Equal(25.0, ToDouble(avg), precision: 8);

        using var divSum = array.AsStackEnumerable().SelectDivide(scalar).Sum();
        Assert.Equal(20.0, ToDouble(divSum), precision: 8);

        using var mulSum = array.AsStackEnumerable().AsParallel().SelectMultiply(scalar).Sum();
        Assert.Equal(500.0, ToDouble(mulSum), precision: 8);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void StackEnumerable_ZipArithmetic_CalculatesCorrectResults()
    {
        using var l0 = CreateFloat(100);
        using var l1 = CreateFloat(60);
        using var r0 = CreateFloat(20);
        using var r1 = CreateFloat(15);

        var left = new[] { l0, l1 };
        var right = new[] { r0, r1 };

        using var zipDivSum = left.AsStackEnumerable()
            .ZipDivide(right.AsStackEnumerable())
            .Sum();
        Assert.Equal(9.0, ToDouble(zipDivSum), precision: 8); // (100/20) + (60/15) = 5 + 4 = 9

        using var zipAddSum = left.AsStackEnumerable()
            .AsParallel()
            .ZipAdd(right.AsStackEnumerable().AsParallel())
            .Sum();
        Assert.Equal(195.0, ToDouble(zipAddSum), precision: 8); // 120 + 75 = 195
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigFloatArray_VectorLinq_SelectAndZip_CalculateCorrectResults()
    {
        const int length = 4;
        const int words = 4;

        using var leftVec = new GpuBigFloatArray(_context, length, words);
        using var rightVec = new GpuBigFloatArray(_context, length, words);

        double[] leftVals = [12.0, -24.0, 36.0, 48.0];
        double[] rightVals = [3.0, 6.0, -9.0, 12.0];

        for (var i = 0; i < length; i++)
        {
            using var l = CreateFloat((long)leftVals[i]);
            using var r = CreateFloat((long)rightVals[i]);
            leftVec.SetElement(i, l);
            rightVec.SetElement(i, r);
        }

        using var scalar = CreateFloat(3);

        // SelectDivide
        using var selectDivExpr = leftVec.AsVectorizedEnumerable().SelectDivide(scalar);
        var selectDivRes = selectDivExpr.Evaluate(_context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(leftVals[i] / 3.0, ToDouble(selectDivRes.GetElement(i)), precision: 8);
        }

        // ZipDivide
        using var zipDivExpr = leftVec.AsVectorizedEnumerable().ZipDivide(rightVec.AsVectorizedEnumerable());
        var zipDivRes = zipDivExpr.Evaluate(_context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(leftVals[i] / rightVals[i], ToDouble(zipDivRes.GetElement(i)), precision: 8);
        }

        // ZipAdd + Sum / Max / Min / Average
        using var zipAddExpr = leftVec.AsVectorizedEnumerable().ZipAdd(rightVec.AsVectorizedEnumerable());
        using var totalSum = zipAddExpr.Sum(_context);
        using var maxVal = leftVec.AsVectorizedEnumerable().Max(_context);
        using var minVal = leftVec.AsVectorizedEnumerable().Min(_context);
        using var avgVal = leftVec.AsVectorizedEnumerable().Average(_context);

        Assert.Equal(84.0, ToDouble(totalSum), precision: 8); // (12+3) + (-24+6) + (36-9) + (48+12) = 15 - 18 + 27 + 60 = 84
        Assert.Equal(48.0, ToDouble(maxVal), precision: 8);
        Assert.Equal(-24.0, ToDouble(minVal), precision: 8);
        Assert.Equal(18.0, ToDouble(avgVal), precision: 8); // (12 - 24 + 36 + 48) / 4 = 72 / 4 = 18

        // Skip + Take + EvaluateTo
        using var slicedExpr = leftVec.AsVectorizedEnumerable().Skip(1).Take(2).SelectMultiply(scalar);
        Assert.Equal(2, slicedExpr.Count());
        using var destArray = new GpuBigFloatArray(_context, 2, slicedExpr.WordsPerElement);
        slicedExpr.EvaluateTo(_context, destArray);
        Assert.Equal(-72.0, ToDouble(destArray.GetElement(0)), precision: 8);
        Assert.Equal(108.0, ToDouble(destArray.GetElement(1)), precision: 8);
    }
}
