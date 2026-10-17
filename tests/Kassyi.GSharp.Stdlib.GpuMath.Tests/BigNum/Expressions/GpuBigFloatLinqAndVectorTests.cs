
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

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigFloatArray_VectorLinq_Comparisons_And_Where_WorkCorrectly()
    {
        const int length = 5;
        const int words = 4;

        using var vec = new GpuBigFloatArray(_context, length, words);
        using var otherVec = new GpuBigFloatArray(_context, length, words);

        // Values: [10.0, -5.0, 20.0 (5 * 2^2), 0.0, 50.0]
        using var v0 = CreateFloat(10, exponent: 0);
        using var v1 = CreateFloat(-5, exponent: 0);
        using var v2 = CreateFloat(5, exponent: 2); // 20.0
        using var v3 = CreateFloat(0, exponent: 0);
        using var v4 = CreateFloat(50, exponent: 0);

        vec.SetElement(0, v0);
        vec.SetElement(1, v1);
        vec.SetElement(2, v2);
        vec.SetElement(3, v3);
        vec.SetElement(4, v4);

        // Other values: [10.0 (5 * 2^1), -2.0, 15.0, 0.0, 100.0]
        using var o0 = CreateFloat(5, exponent: 1); // 10.0
        using var o1 = CreateFloat(-2, exponent: 0);
        using var o2 = CreateFloat(15, exponent: 0);
        using var o3 = CreateFloat(0, exponent: -10);
        using var o4 = CreateFloat(100, exponent: 0);

        otherVec.SetElement(0, o0);
        otherVec.SetElement(1, o1);
        otherVec.SetElement(2, o2);
        otherVec.SetElement(3, o3);
        otherVec.SetElement(4, o4);

        using var scalar20 = CreateFloat(20, exponent: 0);
        using var scalarZero = CreateFloat(0, exponent: 0);

        // Scalar comparisons (note v2 = 5 * 2^2 == 20 * 2^0)
        using var eqExpr = vec.AsVectorizedEnumerable().IsEqualTo(scalar20);
        var eqHost = new uint[length];
        using (var eqDev = eqExpr.Evaluate(_context))
        {
            _context.Provider.CopyDeviceToHost(eqHost, eqDev.DevicePointer, length);
        }

        Assert.Equal([0u, 0u, 1u, 0u, 0u], eqHost);

        // Vector-to-vector comparisons: vec >= otherVec -> [10==10(1), -5>=-2(0), 20>=15(1), 0==0(1), 50>=100(0)]
        using var gteVecExpr = vec.AsVectorizedEnumerable().IsGreaterThanOrEqualTo(otherVec.AsVectorizedEnumerable());
        var gteHost = new uint[length];
        using (var gteDev = gteVecExpr.Evaluate(_context))
        {
            _context.Provider.CopyDeviceToHost(gteHost, gteDev.DevicePointer, length);
        }

        Assert.Equal([1u, 0u, 1u, 1u, 0u], gteHost);

        // Where (> 0) -> [10.0, 20.0, 50.0, 0.0 (padded), 0.0 (padded)], Sum = 80.0
        using var filteredSum = vec.AsVectorizedEnumerable()
            .Where(vec.AsVectorizedEnumerable().IsGreaterThan(scalarZero))
            .Sum(_context);
        Assert.Equal(80.0, ToDouble(filteredSum), precision: 8);

        // Where (> 0) + Take(2) -> [10.0, 20.0]
        using var filteredTakeExpr = vec.AsVectorizedEnumerable()
            .Where(vec.AsVectorizedEnumerable().IsGreaterThan(scalarZero))
            .Take(2);
        var filteredTakeRes = filteredTakeExpr.Evaluate(_context);
        Assert.Equal(10.0, ToDouble(filteredTakeRes.GetElement(0)), precision: 8);
        Assert.Equal(20.0, ToDouble(filteredTakeRes.GetElement(1)), precision: 8);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigFloatArray_MultiChunk_SumMaxMin_ReduceCorrectlyOnGpu()
    {
        const int length = 300; // Crosses 256-element chunk boundary (2 passes)
        const int words = 4;

        using var vec = new GpuBigFloatArray(_context, length, words);
        double expectedSum = 0;
        double expectedMax = double.MinValue;
        double expectedMin = double.MaxValue;

        for (var i = 0; i < length; i++)
        {
            long sig = (i % 3 == 0) ? -(i + 1) : (i + 1);
            int exp = (i % 2 == 0) ? 0 : 1;
            double val = sig * Math.Pow(2.0, exp);
            expectedSum += val;
            if (val > expectedMax) expectedMax = val;
            if (val < expectedMin) expectedMin = val;

            using var elem = CreateFloat(sig, exp, words);
            vec.SetElement(i, elem);
        }

        using var gpuSum = vec.AsVectorizedEnumerable().Sum(_context);
        using var gpuMax = vec.AsVectorizedEnumerable().Max(_context);
        using var gpuMin = vec.AsVectorizedEnumerable().Min(_context);

        Assert.Equal(expectedSum, ToDouble(gpuSum), precision: 8);
        Assert.Equal(expectedMax, ToDouble(gpuMax), precision: 8);
        Assert.Equal(expectedMin, ToDouble(gpuMin), precision: 8);
    }
}
