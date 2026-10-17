
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
    public void StackEnumerable_BalancedTree_OddAndLargeSequences_CalculateCorrectResults()
    {
        // Odd count: 5 elements [5, 15, -10, 25, 30]
        using var o0 = CreateFloat(5);
        using var o1 = CreateFloat(15);
        using var o2 = CreateFloat(-10);
        using var o3 = CreateFloat(25);
        using var o4 = CreateFloat(30);

        var oddArray = new[] { o0, o1, o2, o3, o4 };

        using var oddSum = oddArray.AsStackEnumerable().Sum();
        using var oddMax = oddArray.AsStackEnumerable().Max();
        using var oddMin = oddArray.AsStackEnumerable().Min();
        using var oddAvg = oddArray.AsStackEnumerable().Average();

        Assert.Equal(65.0, ToDouble(oddSum), precision: 8);
        Assert.Equal(30.0, ToDouble(oddMax), precision: 8);
        Assert.Equal(-10.0, ToDouble(oddMin), precision: 8);
        Assert.Equal(13.0, ToDouble(oddAvg), precision: 8);

        // Aggregate with custom binary func: pairwise sum
        using var aggSum = oddArray.AsStackEnumerable().Aggregate(static (a, b) => a + b);
        Assert.Equal(65.0, ToDouble(aggSum), precision: 8);
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

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void StackEnumerable_GenericUnification_GpuBigUInt_And_GpuBigUFloat_CalculateCorrectResults()
    {
        using var u0 = new GpuBigUInt(_context, [10u, 0u, 0u, 0u]);
        using var u1 = new GpuBigUInt(_context, [20u, 0u, 0u, 0u]);
        using var u2 = new GpuBigUInt(_context, [30u, 0u, 0u, 0u]);
        using var u3 = new GpuBigUInt(_context, [40u, 0u, 0u, 0u]);
        using var uScalar = new GpuBigUInt(_context, [5u, 0u, 0u, 0u]);

        var uintArray = new[] { u0, u1, u2, u3 };

        using var uSum = uintArray.AsStackEnumerable().Sum();
        using var uMax = uintArray.AsStackEnumerable().Max();
        using var uMin = uintArray.AsStackEnumerable().Min();
        using var uAvg = uintArray.AsStackEnumerable().Average();
        using var uAgg = uintArray.AsStackEnumerable().Aggregate(static (a, b) => (a + b).Node.Evaluate(a.Workspace));

        Assert.Equal(new BigInteger(100), uSum.ToBigInteger());
        Assert.Equal(new BigInteger(40), uMax.ToBigInteger());
        Assert.Equal(new BigInteger(10), uMin.ToBigInteger());
        Assert.Equal(new BigInteger(25), uAvg.ToBigInteger());
        Assert.Equal(new BigInteger(100), uAgg.ToBigInteger());
        Assert.Equal(4, uintArray.AsStackEnumerable().AsParallel().Count());

        using var uAddSum = uintArray.AsStackEnumerable().SelectAdd(uScalar).Sum();
        using var uSubSum = uintArray.AsStackEnumerable().SelectSubtract(uScalar).Sum();
        using var uMulSum = uintArray.AsStackEnumerable().AsParallel().SelectMultiply(uScalar).Sum();
        using var uDivSum = uintArray.AsStackEnumerable().SelectDivide(uScalar).Sum();

        Assert.Equal(new BigInteger(120), uAddSum.ToBigInteger());
        Assert.Equal(new BigInteger(80), uSubSum.ToBigInteger());
        Assert.Equal(new BigInteger(500), uMulSum.ToBigInteger());
        Assert.Equal(new BigInteger(20), uDivSum.ToBigInteger());

        // GpuBigUFloat sequences
        using var uf0 = new GpuBigUFloat(new GpuBigUInt(_context, [12u, 0u, 0u, 0u]), 0);
        using var uf1 = new GpuBigUFloat(new GpuBigUInt(_context, [24u, 0u, 0u, 0u]), 0);
        using var uf2 = new GpuBigUFloat(new GpuBigUInt(_context, [36u, 0u, 0u, 0u]), 0);
        using var ufScalar = new GpuBigUFloat(new GpuBigUInt(_context, [3u, 0u, 0u, 0u]), 0);

        var ufloatArray = new[] { uf0, uf1, uf2 };

        using var ufSum = ufloatArray.AsStackEnumerable().Sum();
        using var ufMax = ufloatArray.AsStackEnumerable().Max();
        using var ufMin = ufloatArray.AsStackEnumerable().Min();
        using var ufAvg = ufloatArray.AsStackEnumerable().Average();
        using var ufZipAdd = ufloatArray.AsStackEnumerable()
            .ZipAdd(ufloatArray.AsStackEnumerable())
            .SelectDivide(ufScalar)
            .Sum();

        Assert.Equal(new BigInteger(72), ufSum.Significand.ToBigInteger() << ufSum.Exponent);
        Assert.Equal(new BigInteger(36), ufMax.Significand.ToBigInteger());
        Assert.Equal(new BigInteger(12), ufMin.Significand.ToBigInteger());
        var ufAvgDouble = (double)ufAvg.Significand.ToBigInteger() * Math.Pow(2.0, ufAvg.Exponent);
        Assert.Equal(24.0, ufAvgDouble, precision: 8);
        var ufZipAddDouble = (double)ufZipAdd.Significand.ToBigInteger() * Math.Pow(2.0, ufZipAdd.Exponent);
        Assert.Equal(48.0, ufZipAddDouble, precision: 8);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigFloatArray_VectorLinq_HeterogeneousScalarTypes_CalculateCorrectResults()
    {
        const int length = 3;
        const int words = 4;

        using var vec = new GpuBigFloatArray(_context, length, words);
        double[] vals = [10.0, 20.0, 30.0];
        for (var i = 0; i < length; i++)
        {
            using var f = CreateFloat((long)vals[i]);
            vec.SetElement(i, f);
        }

        using var uintScalar = new GpuBigUInt(_context, [5u, 0u, 0u, 0u]);
        using var intScalar = new GpuBigInt(new GpuBigUInt(_context, [4u, 0u, 0u, 0u]), isNegative: true);
        using var ufloatScalar = new GpuBigUFloat(new GpuBigUInt(_context, [3u, 0u, 0u, 0u]), 1); // 3 * 2^1 = 6.0

        using var addUIntExpr = vec.AsVectorizedEnumerable().SelectAdd(uintScalar);
        var addUIntRes = addUIntExpr.Evaluate(_context);

        using var mulIntExpr = vec.AsVectorizedEnumerable().SelectMultiply(intScalar);
        var mulIntRes = mulIntExpr.Evaluate(_context);

        using var subUFloatExpr = vec.AsVectorizedEnumerable().SelectSubtract(ufloatScalar);
        var subUFloatRes = subUFloatExpr.Evaluate(_context);

        for (var i = 0; i < length; i++)
        {
            Assert.Equal(vals[i] + 5.0, ToDouble(addUIntRes.GetElement(i)), precision: 8);
            Assert.Equal(vals[i] * -4.0, ToDouble(mulIntRes.GetElement(i)), precision: 8);
            Assert.Equal(vals[i] - 6.0, ToDouble(subUFloatRes.GetElement(i)), precision: 8);
        }
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigFloatArray_VectorLinq_ElementaryAndTranscendental_CalculateCorrectResults()
    {
        const int length = 3;
        const int words = 4;

        using var vec = new GpuBigFloatArray(_context, length, words);
        using var expVec = new GpuBigFloatArray(_context, length, words);

        double[] vals = [4.0, 9.0, 16.0];
        double[] exps = [2.0, 0.5, 1.0];

        for (var i = 0; i < length; i++)
        {
            using var v = CreateFloat((long)vals[i], 0, words);
            vec.SetElement(i, v);
        }

        using var e0 = CreateFloat(2, 0, words); // 2.0
        using var e1 = CreateFloat(1, -1, words); // 0.5
        using var e2 = CreateFloat(1, 0, words); // 1.0
        expVec.SetElement(0, e0);
        expVec.SetElement(1, e1);
        expVec.SetElement(2, e2);

        // 1. SelectSqrt
        using var sqrtExpr = vec.AsVectorizedEnumerable().SelectSqrt();
        var sqrtRes = sqrtExpr.Evaluate(_context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(Math.Sqrt(vals[i]), ToDouble(sqrtRes.GetElement(i)), precision: 6);
        }

        // 2. SelectRsqrt
        using var rsqrtExpr = vec.AsVectorizedEnumerable().SelectRsqrt();
        var rsqrtRes = rsqrtExpr.Evaluate(_context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(1.0 / Math.Sqrt(vals[i]), ToDouble(rsqrtRes.GetElement(i)), precision: 6);
        }

        // 3. SelectReciprocal
        using var recipExpr = vec.AsVectorizedEnumerable().SelectReciprocal();
        var recipRes = recipExpr.Evaluate(_context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(1.0 / vals[i], ToDouble(recipRes.GetElement(i)), precision: 6);
        }

        // 4. SelectDivideByUInt32 (divides significand by uint32 while preserving exponent, matching GpuBigFloat.DivideByUInt32)
        using var divU32Expr = vec.AsVectorizedEnumerable().SelectDivideByUInt32(2u);
        var divU32Res = divU32Expr.Evaluate(_context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(Math.Floor(vals[i] / 2.0), ToDouble(divU32Res.GetElement(i)), precision: 8);
        }

        // 5. SelectLog & SelectExp
        using var logExpr = vec.AsVectorizedEnumerable().SelectLog();
        var logRes = logExpr.Evaluate(_context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(Math.Log(vals[i]), ToDouble(logRes.GetElement(i)), precision: 5);
        }

        using var roundTripExpExpr = vec.AsVectorizedEnumerable().SelectLog().SelectExp();
        var roundTripExpRes = roundTripExpExpr.Evaluate(_context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(vals[i], ToDouble(roundTripExpRes.GetElement(i)), precision: 4);
        }

        // 6. SelectPow<TScalar> (with GpuBigUInt scalar 2)
        using var powScalar = new GpuBigUInt(_context, [2u, 0u, 0u, 0u]);
        using var powScalarExpr = vec.AsVectorizedEnumerable().SelectPow(powScalar);
        var powScalarRes = powScalarExpr.Evaluate(_context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(Math.Pow(vals[i], 2.0), ToDouble(powScalarRes.GetElement(i)), precision: 3);
        }

        // 7. ZipPow
        using var zipPowExpr = vec.AsVectorizedEnumerable().ZipPow(expVec.AsVectorizedEnumerable());
        var zipPowRes = zipPowExpr.Evaluate(_context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(Math.Pow(vals[i], exps[i]), ToDouble(zipPowRes.GetElement(i)), precision: 4);
        }
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void GpuBigFloatArray_VectorLinq_ElementaryAndTranscendental_SadPath_ThrowsExpectedExceptions()
    {
        using var zeroVec = new GpuBigFloatArray(_context, 2, 4);
        using var z0 = CreateFloat(0);
        using var z1 = CreateFloat(4);
        zeroVec.SetElement(0, z0);
        zeroVec.SetElement(1, z1);

        using var divZeroExpr = zeroVec.AsVectorizedEnumerable().SelectDivideByUInt32(0u);
        Assert.Throws<DivideByZeroException>(() => divZeroExpr.Evaluate(_context));

        using var recipZeroExpr = zeroVec.AsVectorizedEnumerable().SelectReciprocal();
        Assert.Throws<DivideByZeroException>(() => recipZeroExpr.Evaluate(_context));

        using var rsqrtZeroExpr = zeroVec.AsVectorizedEnumerable().SelectRsqrt();
        Assert.Throws<DivideByZeroException>(() => rsqrtZeroExpr.Evaluate(_context));

        using var negVec = new GpuBigFloatArray(_context, 2, 4);
        using var n0 = CreateFloat(-4);
        negVec.SetElement(0, n0);
        negVec.SetElement(1, z1);

        using var sqrtNegExpr = negVec.AsVectorizedEnumerable().SelectSqrt();
        Assert.Throws<InvalidOperationException>(() => sqrtNegExpr.Evaluate(_context));

        using var logNegExpr = negVec.AsVectorizedEnumerable().SelectLog();
        Assert.Throws<InvalidOperationException>(() => logNegExpr.Evaluate(_context));

        using var shortVec = new GpuBigFloatArray(_context, 1, 4);
        shortVec.SetElement(0, z1);
        using var mismatchedZipPow = zeroVec.AsVectorizedEnumerable().ZipPow(shortVec.AsVectorizedEnumerable());
        Assert.Throws<ArgumentException>(() => mismatchedZipPow.Evaluate(_context));
    }
}
