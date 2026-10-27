
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
    public void GpuBigFloatArray_EmptyVector_AggregatesMatchSystemLinq()
    {
        // The reference: System.Linq over an empty double sequence. Max / Min / Average throw
        // InvalidOperationException; Sum and Count are 0 (#633 decision 2, the same rule as the integer vectors).
        // mutant: FloatVectorReductionOrchestrator.cs `if (kind == FloatReductionKind.Sum)` (empty) -> `if (kind == FloatReductionKind.Max)`
        // input: this test (Max returns 0 instead of throwing, and Sum throws)
        // mutant: GpuBigFloatVectorLinqExtensions.cs Average `if (source.Length == 0)` -> `if (source.Length < 0)`
        // input: Average (divides 0 by 0 instead of throwing)
        var reference = Array.Empty<double>();
        var expectedMax = Record.Exception(() => reference.Max());
        var expectedMin = Record.Exception(() => reference.Min());
        var expectedAverage = Record.Exception(() => reference.Average());
        Assert.IsType<InvalidOperationException>(expectedMax);
        Assert.IsType<InvalidOperationException>(expectedMin);
        Assert.IsType<InvalidOperationException>(expectedAverage);

        using var empty = new GpuBigFloatArray(_context, 0, 4);

        var max = Assert.Throws<InvalidOperationException>(() => empty.AsVectorizedEnumerable().Max(_context));
        var min = Assert.Throws<InvalidOperationException>(() => empty.AsVectorizedEnumerable().Min(_context));
        var average = Assert.Throws<InvalidOperationException>(() => empty.AsVectorizedEnumerable().Average(_context));
        Assert.Equal(expectedMax.Message, max.Message);
        Assert.Equal(expectedMin.Message, min.Message);
        Assert.Equal(expectedAverage.Message, average.Message);

        using var sum = empty.AsVectorizedEnumerable().Sum(_context);
        Assert.Equal(reference.Sum(), ToDouble(sum));
        Assert.False(sum.IsNegative);
        Assert.Equal(reference.Length, empty.AsVectorizedEnumerable().Count());
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigFloatArray_EmptyAfterSkipAndSelect_AggregatesMatchSystemLinq()
    {
        // An empty vector that is not a leaf: Skip(length) leaves nothing, and SelectAdd would launch an empty grid.
        // The reference is System.Linq over the same empty selection.
        // mutant: FloatVectorBinaryScalarExpressions.cs `if (evalCount > 0)` -> `if (evalCount >= 0)`
        // input: the direct Evaluate below (CudaException: invalid value)
        // mutant: FloatVectorReductionOrchestrator.cs `if (source.Length == 0)` -> `if (source.Length < 0)`
        // input: none alone, the empty SelectAdd now launches nothing; with the first mutant as well, Sum fails
        double[] values = [3, -4, 5];
        using var vec = new GpuBigFloatArray(_context, values.Length, 4);
        for (var i = 0; i < values.Length; i++)
        {
            using var element = CreateFloat((long)values[i]);
            vec.SetElement(i, element);
        }

        using var one = CreateFloat(1);
        var reference = values.Skip(values.Length).Select(static v => v + 1).ToArray();

        using (var direct = vec.AsVectorizedEnumerable().Skip(values.Length).SelectAdd(one))
        {
            Assert.Equal(0, direct.Evaluate(_context).Length);
        }

        using var expr = vec.AsVectorizedEnumerable().Skip(values.Length).SelectAdd(one);
        using var sum = expr.Sum(_context);
        Assert.Equal(reference.Sum(), ToDouble(sum));
        Assert.False(sum.IsNegative);
        Assert.IsType<InvalidOperationException>(Record.Exception(() => reference.Average()));
        Assert.Throws<InvalidOperationException>(() => expr.Max(_context));
        Assert.Throws<InvalidOperationException>(() => expr.Min(_context));
        Assert.Throws<InvalidOperationException>(() => expr.Average(_context));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigFloatArray_EmptyVector_CompareAndDivideByUInt32_ReturnEmptyResults()
    {
        // Comparisons of empty float vectors launch nothing (a grid of 0 blocks is an invalid launch) and give empty
        // results, as System.Linq's Select / Zip over empty sequences do. The division by a 32-bit divisor is a host
        // loop that runs no iteration.
        // Both comparisons already returned early for Length == 0 before #633, and the division's host loop already
        // skipped an empty vector, so this test was green before the change; it fixes the behavior.
        // mutant: FloatVectorCompareExpressions.cs FloatVectorCompareScalarExpression `if (Length == 0)` -> `if (Length < 0)`
        // input: the scalar comparison
        // mutant: FloatVectorCompareExpressions.cs FloatVectorCompareVectorExpression `if (Length == 0)` -> `if (Length < 0)`
        // input: the vector comparison
        using var empty = new GpuBigFloatArray(_context, 0, 4);
        using var other = new GpuBigFloatArray(_context, 0, 4);
        using var scalar = CreateFloat(3);
        var reference = Array.Empty<double>();

        using var scalarCompare = empty.AsVectorizedEnumerable().IsLessThan(scalar);
        using var vectorCompare = empty.AsVectorizedEnumerable().IsEqualTo(other.AsVectorizedEnumerable());
        using var divide = empty.AsVectorizedEnumerable().SelectDivideByUInt32(3u);

        Assert.Equal(reference.Select(static v => v < 3).Count(), scalarCompare.Evaluate(_context).Length);
        Assert.Equal(reference.Zip(reference, static (a, b) => a == b).Count(), vectorCompare.Evaluate(_context).Length);
        Assert.Equal(reference.Select(static v => v / 3).Count(), divide.Evaluate(_context).Length);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigFloatArray_CompareNegativeZero_EqualsZero()
    {
        // The float comparison takes the signs from its own sign arrays; a negative zero (sign 1, significand 0) must
        // compare equal to zero, as the integer comparison does. Expected values: -0.0 == 0.0 in IEEE / System.Double.
        using var vec = new GpuBigFloatArray(_context, 2, 4);
        using var other = new GpuBigFloatArray(_context, 2, 4);
        using var negativeZero = new GpuBigFloat(new GpuBigUInt(_context, new uint[4]), 0, isNegative: true);
        using var zero = CreateFloat(0);
        vec.SetElement(0, negativeZero);
        vec.SetElement(1, zero);
        other.SetElement(0, zero);
        other.SetElement(1, negativeZero);
        var signsHost = new uint[2];
        _context.Provider.CopyDeviceToHost(signsHost, vec.Signs.DevicePointer, 2);
        Assert.Equal([1u, 0u], signsHost); // the negative zero is really stored with sign 1

        using var eqVector = vec.AsVectorizedEnumerable().IsEqualTo(other.AsVectorizedEnumerable());
        using var ltVector = vec.AsVectorizedEnumerable().IsLessThan(other.AsVectorizedEnumerable());
        using var eqScalar = vec.AsVectorizedEnumerable().IsEqualTo(negativeZero);
        var eqVectorHost = new uint[2];
        var ltVectorHost = new uint[2];
        var eqScalarHost = new uint[2];
        _context.Provider.CopyDeviceToHost(eqVectorHost, eqVector.Evaluate(_context).DevicePointer, 2);
        _context.Provider.CopyDeviceToHost(ltVectorHost, ltVector.Evaluate(_context).DevicePointer, 2);
        _context.Provider.CopyDeviceToHost(eqScalarHost, eqScalar.Evaluate(_context).DevicePointer, 2);

        Assert.Equal([-0.0 == 0.0 ? 1u : 0u, 0.0 == -0.0 ? 1u : 0u], eqVectorHost);
        Assert.Equal([-0.0 < 0.0 ? 1u : 0u, 0.0 < -0.0 ? 1u : 0u], ltVectorHost);
        Assert.Equal([-0.0 == -0.0 ? 1u : 0u, 0.0 == -0.0 ? 1u : 0u], eqScalarHost);
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

    // A slice must lie inside the array; the outer check reports the offending range as `elementOffset`.
    // The inner GpuBigUIntArray.Slice throws the same exception type without a ParamName, so ParamName tells the
    // outer check apart from the inner one.
    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    // mutant: GpuBigFloatArray.cs:147 `elementOffset < 0` -> `<= 0`, `> Length` -> `>= Length`
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    // mutant: GpuBigFloatArray.cs:147 `elementCount < 0` -> `<= 0`
    [InlineData(1, 0)]
    public void GpuBigFloatArray_Slice_InsideBounds_Succeeds(int offset, int count)
    {
        using var array = new GpuBigFloatArray(_context, 4, 2);

        // The slice is a view of the array's memory; it is not disposed separately.
        var slice = array.Slice(offset, count);

        Assert.Equal(count, slice.Length);
        Assert.Equal(count, slice.Exponents.Length);
        Assert.Equal(count, slice.Signs.Length);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    // mutant: GpuBigFloatArray.cs:147 `||` -> `&&` (either), L149 `throw` removed
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    // mutant: GpuBigFloatArray.cs:147 `elementOffset + elementCount` -> `elementOffset - elementCount`
    [InlineData(1, 4)]
    public void GpuBigFloatArray_Slice_OutsideBounds_Throws(int offset, int count)
    {
        using var array = new GpuBigFloatArray(_context, 4, 2);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => array.Slice(offset, count));
        Assert.Equal("elementOffset", error.ParamName);
    }

    // The three component arrays describe the same elements, so their lengths must agree.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void GpuBigFloatArray_InternalCtor_MismatchedLengths_Throws()
    {
        // The constructor throws before it takes ownership, so the test disposes the arrays.
        using var significands = new GpuBigUIntArray(_context, 2, 4);
        using var exponents = new GpuBigUIntArray(_context, 2, 1);
        using var signs = new GpuBigUIntArray(_context, 3, 1);

        // mutant: GpuBigFloatArray.cs:72 `||` -> `&&`, L74 `throw` removed
        Assert.Throws<ArgumentException>(() => new GpuBigFloatArray(significands, exponents, signs));
    }

    // SetElement replaces the whole element: a shorter value leaves no stale upper words, and it never writes
    // past the element into its neighbour.
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigFloatArray_SetElement_ShorterAndLongerValues()
    {
        using var array = new GpuBigFloatArray(_context, 2, 4);

        // 1. A 4-word all-ones value, then a 1-word 5: the element reads back as exactly 5.
        // mutant: GpuBigFloatArray.cs:130 `valueWords < WordsPerElement` -> `>` / negated, L132 ClearMemoryAsync removed
        using var allOnes = new GpuBigFloat(
            new GpuBigUInt(_context, [0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu, 0xFFFFFFFFu]), 0, false);
        using var five = new GpuBigFloat(new GpuBigUInt(_context, [5u]), 3, true);
        array.SetElement(0, allOnes);
        array.SetElement(0, five);

        // GetElement returns views of the array's memory; they are not disposed.
        var element0 = array.GetElement(0);
        Assert.Equal(new BigInteger(5), element0.UFloat.Significand.ToBigInteger());
        Assert.Equal(3, element0.Exponent);
        Assert.True(element0.IsNegative);

        // 2. A 6-word value into element 0 must not touch element 1 (what element 0 holds is checked by
        // GpuBigFloatArray_SetElement_RoundsWideValuesTowardZero).
        // mutant: GpuBigFloatArray.cs:128 `valueWords <= WordsPerElement` -> `valueWords <= WordsPerElement + 2` (copies 6 words over element 1)
        using var seven = new GpuBigFloat(new GpuBigUInt(_context, [7u, 0u, 0u, 0u]), -2, false);
        array.SetElement(1, seven);
        using var wide = new GpuBigFloat(new GpuBigUInt(_context, [1u, 2u, 3u, 4u, 5u, 6u]), 0, false);
        array.SetElement(0, wide);

        var element1 = array.GetElement(1);
        Assert.Equal(new BigInteger(7), element1.UFloat.Significand.ToBigInteger());
        Assert.Equal(-2, element1.Exponent);
        Assert.False(element1.IsNegative);
    }

    /// <summary>
    /// Values stored into a 4-word element (128-bit significand): significand words (little-endian), exponent, sign.
    /// </summary>
    private static readonly Dictionary<string, (uint[] Words, int Exponent, bool IsNegative)> s_setElementCases = new()
    {
        // 2^31 as a 6-word significand whose only set bit is its top bit (#629: the copy of the low 4 words stored 0).
        ["SixWordsTopBitOnly"] = ([0u, 0u, 0u, 0u, 0u, 0x8000_0000u], -160, false),
        // The top two words are zero: the value has 128 bits and must be stored exactly.
        ["SixWordsThatFit"] = ([0x1234_5678u, 0x9ABC_DEF0u, 0x0FED_CBA9u, 0x8765_4321u, 0u, 0u], 5, false),
        // Set bits below the top 128 bits are dropped toward zero; the sign is kept.
        ["SixWordsTruncated"] = ([0xDEAD_BEEFu, 0x1234_5678u, 1u, 2u, 3u, 0x4000_0000u], -7, true),
        // Zero in 6 words is stored as positive zero.
        ["SixWordsZero"] = ([0u, 0u, 0u, 0u, 0u, 0u], 9, true),
        // A value of 4 words or fewer is copied as it is.
        ["TwoWords"] = ([7u, 9u], -3, false),
        ["FourWords"] = ([1u, 2u, 3u, 4u], 11, true),
    };

    public static TheoryData<string> SetElementCases()
    {
        var data = new TheoryData<string>();
        foreach (var name in s_setElementCases.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    // The element holds RZ_128(v): v itself when its significand has at most 128 bits, otherwise v rounded toward zero
    // to 128 bits (#629). The neighbouring elements do not change.
    // mutant: GpuBigFloatArray.cs:128 `valueWords <= WordsPerElement` -> `true` (SixWordsTopBitOnly, SixWordsTruncated,
    // SixWordsThatFit, SixWordsZero: the wide value is copied as it is, over its neighbour)
    // mutant: GpuBigFloatArray.cs:149 `exponent = rounded.Exponent` -> `exponent = value.UFloat.Exponent` (SixWordsTopBitOnly,
    // SixWordsTruncated)
    [Theory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(SetElementCases))]
    public void GpuBigFloatArray_SetElement_RoundsWideValuesTowardZero(string caseName)
    {
        var (words, exponent, isNegative) = s_setElementCases[caseName];
        using var array = new GpuBigFloatArray(_context, 3, 4);
        using var before = new GpuBigFloat(new GpuBigUInt(_context, [11u, 0u, 0u, 0u]), -1, false);
        using var after = new GpuBigFloat(new GpuBigUInt(_context, [13u, 0u, 0u, 0u]), 2, true);
        array.SetElement(0, before);
        array.SetElement(2, after);

        using var value = new GpuBigFloat(new GpuBigUInt(_context, words), exponent, isNegative);
        array.SetElement(1, value);

        // The expected value comes from the input words: RZ_128(|v|) (|v| itself when it fits) with the sign of v.
        var (rzSig, rzExp) = BigFloatReference.RoundTowardZero(
            FromWords(words) << Math.Max(exponent, 0), BigInteger.One << Math.Max(-exponent, 0), 128);
        var expectedNum = rzExp >= 0 ? rzSig << (int)rzExp : rzSig;
        var expectedDen = rzExp >= 0 ? BigInteger.One : BigInteger.One << (int)-rzExp;

        // GetElement returns views of the array's memory; they are not disposed.
        AssertElement(array.GetElement(1), isNegative ? -expectedNum : expectedNum, expectedDen, $"{caseName} element 1");
        AssertElement(array.GetElement(0), 11, 2, $"{caseName} element 0");
        AssertElement(array.GetElement(2), -52, 1, $"{caseName} element 2");
    }

    // Rounding a wide value moves its top bit to the top of the element and raises the exponent by the same shift; an
    // exponent above int.MaxValue throws instead of wrapping around (#661 §4.10).
    // mutant: GSharpKernelFloatProvider.cs:70 `checked((int)(exponent - shift))` -> `unchecked(...)`
    [Fact]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.SadPath)]
    public void GpuBigFloatArray_SetElement_ExponentOutOfRange_Throws()
    {
        using var array = new GpuBigFloatArray(_context, 1, 4);

        // 6 words with the top bit set: the shift is 128 - 192 = -64, so the exponent would be int.MaxValue - 10 + 64.
        using var wide = new GpuBigFloat(
            new GpuBigUInt(_context, [1u, 0u, 0u, 0u, 0u, 0x8000_0000u]), int.MaxValue - 10, false);
        Assert.Throws<OverflowException>(() => array.SetElement(0, wide));

        // The largest exponent that fits is stored: int.MaxValue - 64 + 64.
        using var fits = new GpuBigFloat(
            new GpuBigUInt(_context, [1u, 0u, 0u, 0u, 0u, 0x8000_0000u]), int.MaxValue - 64, false);
        array.SetElement(0, fits);
        Assert.Equal(int.MaxValue, array.GetElement(0).Exponent);
    }

    // The element equals num / den exactly, and zero is stored as positive zero.
    private static void AssertElement(GpuBigFloat element, BigInteger num, BigInteger den, string label)
    {
        var sig = element.UFloat.Significand.ToBigInteger();
        Assert.True(
            BigFloatReference.EqualsRational(sig, element.Exponent, element.IsNegative, num, den)
            && (!element.IsNegative || !sig.IsZero),
            $"{label}: expected {num}/{den}, actual {BigFloatReference.Describe(sig, element.Exponent, element.IsNegative)}");
    }

    private static BigInteger FromWords(uint[] words)
    {
        var value = BigInteger.Zero;
        for (var i = words.Length - 1; i >= 0; i--)
        {
            value = (value << 32) | words[i];
        }

        return value;
    }

    private static void AssertExactSum<T>(string operation, BigInteger num, BigInteger den, T actual)
        where T : struct, IGpuBigNumber<T>
    {
        var sig = actual.Magnitude.ToBigInteger();
        Assert.True(
            BigFloatReference.EqualsRational(sig, actual.Exponent, actual.IsNegative, num, den),
            $"{typeof(T).Name} {operation}: expected {num}/{den}, actual {BigFloatReference.Describe(sig, actual.Exponent, actual.IsNegative)}");
    }

    private T CreateNumber<T>(uint value)
        where T : struct, IGpuBigNumber<T> =>
        T.Create(new GpuBigUInt(_context, [value, 0u, 0u, 0u]), 0, false);

    // Each Select / Zip overload applies the operation it names, on the parallel and the sequential enumerable.
    // a = [6, 9], scalar 3, b = [3, 2]: every sum differs from the sum of the swapped operation (+ <-> -, * <-> /).
    // a >= b element-wise, so the unsigned subtraction stays non-negative (#629 is not involved).
    private void AssertSelectZipOverloads<T>()
        where T : struct, IGpuBigNumber<T>
    {
        var a = new[] { CreateNumber<T>(6), CreateNumber<T>(9) };
        var b = new[] { CreateNumber<T>(3), CreateNumber<T>(2) };
        var scalar = CreateNumber<T>(3);

        // mutant: GpuBigNumberStackLinqExtensions.cs:47 / 55 / 63 / 71 (parallel Select*), and the generic operators
        // GpuBigFloat.cs:312 / GpuBigUFloat.cs:343 `left - right` -> `left + right`, GpuBigUFloat.cs:347 `*` -> `/`
        AssertExactSum("parallel SelectAdd", 21, 1, a.AsStackEnumerable().AsParallel().SelectAdd(scalar).Sum());
        AssertExactSum("parallel SelectSubtract", 9, 1, a.AsStackEnumerable().AsParallel().SelectSubtract(scalar).Sum());
        AssertExactSum("parallel SelectMultiply", 45, 1, a.AsStackEnumerable().AsParallel().SelectMultiply(scalar).Sum());
        AssertExactSum("parallel SelectDivide", 5, 1, a.AsStackEnumerable().AsParallel().SelectDivide(scalar).Sum());

        // mutant: GpuBigNumberStackLinqExtensions.cs:206 / 215 / 224 / 233 (sequential Select*)
        AssertExactSum("SelectAdd", 21, 1, a.AsStackEnumerable().SelectAdd(scalar).Sum());
        AssertExactSum("SelectSubtract", 9, 1, a.AsStackEnumerable().SelectSubtract(scalar).Sum());
        AssertExactSum("SelectMultiply", 45, 1, a.AsStackEnumerable().SelectMultiply(scalar).Sum());
        AssertExactSum("SelectDivide", 5, 1, a.AsStackEnumerable().SelectDivide(scalar).Sum());

        // mutant: GpuBigNumberStackLinqExtensions.cs:281 / 300 / 319 / 338 (parallel Zip*)
        AssertExactSum("parallel ZipAdd", 20, 1, a.AsStackEnumerable().AsParallel().ZipAdd(b.AsStackEnumerable().AsParallel()).Sum());
        AssertExactSum("parallel ZipSubtract", 10, 1, a.AsStackEnumerable().AsParallel().ZipSubtract(b.AsStackEnumerable().AsParallel()).Sum());
        AssertExactSum("parallel ZipMultiply", 36, 1, a.AsStackEnumerable().AsParallel().ZipMultiply(b.AsStackEnumerable().AsParallel()).Sum());
        AssertExactSum("parallel ZipDivide", 13, 2, a.AsStackEnumerable().AsParallel().ZipDivide(b.AsStackEnumerable().AsParallel()).Sum());

        // mutant: GpuBigNumberStackLinqExtensions.cs:396 / 415 / 434 / 453 (sequential Zip*)
        AssertExactSum("ZipAdd", 20, 1, a.AsStackEnumerable().ZipAdd(b.AsStackEnumerable()).Sum());
        AssertExactSum("ZipSubtract", 10, 1, a.AsStackEnumerable().ZipSubtract(b.AsStackEnumerable()).Sum());
        AssertExactSum("ZipMultiply", 36, 1, a.AsStackEnumerable().ZipMultiply(b.AsStackEnumerable()).Sum());
        AssertExactSum("ZipDivide", 13, 2, a.AsStackEnumerable().ZipDivide(b.AsStackEnumerable()).Sum());
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigFloat_SelectZip_AllOverloads() => AssertSelectZipOverloads<GpuBigFloat>();

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigUFloat_SelectZip_AllOverloads() => AssertSelectZipOverloads<GpuBigUFloat>();

    // An unsigned element-wise difference below zero throws OverflowException when the sequence is evaluated, on the
    // parallel and the sequential enumerable (#629). a = [6, 2], scalar 3, b = [3, 5]: the second element of each
    // difference is negative, the first is not.
    // mutant: GpuBigFloatExpressionVisitor.cs:74 `if (!left.CanBeNegative && res.IsNegative)` -> `if (false)`
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void GpuBigUFloat_SelectZipSubtract_NegativeElement_ThrowsOverflow()
    {
        var a = new[] { CreateNumber<GpuBigUFloat>(6), CreateNumber<GpuBigUFloat>(2) };
        var b = new[] { CreateNumber<GpuBigUFloat>(3), CreateNumber<GpuBigUFloat>(5) };
        var scalar = CreateNumber<GpuBigUFloat>(3);

        Assert.Throws<OverflowException>(() => a.AsStackEnumerable().AsParallel().SelectSubtract(scalar).Sum());
        Assert.Throws<OverflowException>(() => a.AsStackEnumerable().SelectSubtract(scalar).Sum());
        Assert.Throws<OverflowException>(() => a.AsStackEnumerable().AsParallel().ZipSubtract(b.AsStackEnumerable().AsParallel()).Sum());
        Assert.Throws<OverflowException>(() => a.AsStackEnumerable().ZipSubtract(b.AsStackEnumerable()).Sum());
    }
}
