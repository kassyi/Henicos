namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

using static Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions.GpuBigFloatTestHelpers;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigFloatVectorLinqTests : GpuBigFloatWorkspaceTestBase
{
    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigFloatArray_VectorLinq_SelectAndZip_CalculateCorrectResults()
    {
        const int length = 4;
        const int words = 4;
        using var leftVec = new GpuBigFloatArray(Context, length, words);
        using var rightVec = new GpuBigFloatArray(Context, length, words);

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
        var selectDivRes = selectDivExpr.Evaluate(Context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(leftVals[i] / 3.0, ToDouble(selectDivRes.GetElement(i)), precision: 8);
        }

        // ZipDivide
        using var zipDivExpr = leftVec.AsVectorizedEnumerable().ZipDivide(rightVec.AsVectorizedEnumerable());
        var zipDivRes = zipDivExpr.Evaluate(Context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(leftVals[i] / rightVals[i], ToDouble(zipDivRes.GetElement(i)), precision: 8);
        }

        // ZipAdd + Sum / Max / Min / Average
        using var zipAddExpr = leftVec.AsVectorizedEnumerable().ZipAdd(rightVec.AsVectorizedEnumerable());
        using var totalSum = zipAddExpr.Sum(Context);
        using var maxVal = leftVec.AsVectorizedEnumerable().Max(Context);
        using var minVal = leftVec.AsVectorizedEnumerable().Min(Context);
        using var avgVal = leftVec.AsVectorizedEnumerable().Average(Context);

        // (12+3) + (-24+6) + (36-9) + (48+12) = 15 - 18 + 27 + 60 = 84
        Assert.Equal(84.0, ToDouble(totalSum), precision: 8);
        Assert.Equal(48.0, ToDouble(maxVal), precision: 8);
        Assert.Equal(-24.0, ToDouble(minVal), precision: 8);
        Assert.Equal(18.0, ToDouble(avgVal), precision: 8); // (12 - 24 + 36 + 48) / 4 = 72 / 4 = 18

        // Skip + Take + EvaluateTo
        using var slicedExpr = leftVec.AsVectorizedEnumerable().Skip(1).Take(2).SelectMultiply(scalar);
        Assert.Equal(2, slicedExpr.Count());
        using var destArray = new GpuBigFloatArray(Context, 2, slicedExpr.WordsPerElement);
        slicedExpr.EvaluateTo(Context, destArray);
        Assert.Equal(-72.0, ToDouble(destArray.GetElement(0)), precision: 8);
        Assert.Equal(108.0, ToDouble(destArray.GetElement(1)), precision: 8);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void VectorLinq_ComparisonsAndWhere_ProducesExpectedResults()
    {
        const int length = 5;
        const int words = 4;
        using var vec = new GpuBigFloatArray(Context, length, words);
        using var otherVec = new GpuBigFloatArray(Context, length, words);

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
        var eqDev = eqExpr.Evaluate(Context);
        Context.Provider.CopyDeviceToHost(eqHost, eqDev.DevicePointer, length);

        Assert.Equal([0u, 0u, 1u, 0u, 0u], eqHost);

        // Vector-to-vector comparisons: vec >= otherVec -> [10==10(1), -5>=-2(0), 20>=15(1), 0==0(1), 50>=100(0)]
        using var gteVecExpr = vec.AsVectorizedEnumerable().IsGreaterThanOrEqualTo(otherVec.AsVectorizedEnumerable());
        var gteHost = new uint[length];
        var gteDev = gteVecExpr.Evaluate(Context);
        Context.Provider.CopyDeviceToHost(gteHost, gteDev.DevicePointer, length);

        Assert.Equal([1u, 0u, 1u, 1u, 0u], gteHost);

        // Where (> 0) -> [10.0, 20.0, 50.0] (the count, 3, is known on the device), Sum = 80.0
        using var filteredSum = vec.AsVectorizedEnumerable()
            .Where(vec.AsVectorizedEnumerable().IsGreaterThan(scalarZero))
            .Sum(Context);
        Assert.Equal(80.0, ToDouble(filteredSum), precision: 8);

        // Where (> 0) + Take(2) -> [10.0, 20.0]
        using var filteredTakeExpr = vec.AsVectorizedEnumerable()
            .Where(vec.AsVectorizedEnumerable().IsGreaterThan(scalarZero))
            .Take(2);
        var filteredTakeRes = filteredTakeExpr.Evaluate(Context);
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
        // mutant: FloatVectorReductionOrchestrator.cs `if (kind == FloatReductionKind.Sum)` (empty) ->
        // `if (kind == FloatReductionKind.Max)`
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

        using var empty = new GpuBigFloatArray(Context, 0, 4);

        var max = Assert.Throws<InvalidOperationException>(() => empty.AsVectorizedEnumerable().Max(Context));
        var min = Assert.Throws<InvalidOperationException>(() => empty.AsVectorizedEnumerable().Min(Context));
        var average = Assert.Throws<InvalidOperationException>(() => empty.AsVectorizedEnumerable().Average(Context));
        Assert.Equal(expectedMax.Message, max.Message);
        Assert.Equal(expectedMin.Message, min.Message);
        Assert.Equal(expectedAverage.Message, average.Message);

        using var sum = empty.AsVectorizedEnumerable().Sum(Context);
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
        using var vec = new GpuBigFloatArray(Context, values.Length, 4);
        for (var i = 0; i < values.Length; i++)
        {
            using var element = CreateFloat((long)values[i]);
            vec.SetElement(i, element);
        }

        using var one = CreateFloat(1);
        var reference = values.Skip(values.Length).Select(static v => v + 1).ToArray();

        using (var direct = vec.AsVectorizedEnumerable().Skip(values.Length).SelectAdd(one))
        {
            Assert.Equal(0, direct.Evaluate(Context).Length);
        }

        using var expr = vec.AsVectorizedEnumerable().Skip(values.Length).SelectAdd(one);
        using var sum = expr.Sum(Context);
        Assert.Equal(reference.Sum(), ToDouble(sum));
        Assert.False(sum.IsNegative);
        Assert.IsType<InvalidOperationException>(Record.Exception(() => reference.Average()));
        Assert.Throws<InvalidOperationException>(() => expr.Max(Context));
        Assert.Throws<InvalidOperationException>(() => expr.Min(Context));
        Assert.Throws<InvalidOperationException>(() => expr.Average(Context));
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
        // mutant: FloatVectorCompareExpressions.cs FloatVectorCompareScalarExpression
        // `if (Length == 0)` -> `if (Length < 0)`
        // input: the scalar comparison
        // mutant: FloatVectorCompareExpressions.cs FloatVectorCompareVectorExpression
        // `if (Length == 0)` -> `if (Length < 0)`
        // input: the vector comparison
        using var empty = new GpuBigFloatArray(Context, 0, 4);
        using var other = new GpuBigFloatArray(Context, 0, 4);
        using var scalar = CreateFloat(3);
        var reference = Array.Empty<double>();

        using var scalarCompare = empty.AsVectorizedEnumerable().IsLessThan(scalar);
        using var vectorCompare = empty.AsVectorizedEnumerable().IsEqualTo(other.AsVectorizedEnumerable());
        using var divide = empty.AsVectorizedEnumerable().SelectDivideByUInt32(3u);

        Assert.Equal(reference.Select(static v => v < 3).Count(), scalarCompare.Evaluate(Context).Length);
        Assert.Equal(
            reference.Zip(reference, static (a, b) => a == b).Count(),
            vectorCompare.Evaluate(Context).Length);
        Assert.Equal(reference.Select(static v => v / 3).Count(), divide.Evaluate(Context).Length);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void GpuBigFloatArray_CompareNegativeZero_EqualsZero()
    {
        // The float comparison takes the signs from its own sign arrays; a negative zero (sign 1, significand 0) must
        // compare equal to zero, as the integer comparison does. Expected values: -0.0 == 0.0 in IEEE / System.Double.
        using var vec = new GpuBigFloatArray(Context, 2, 4);
        using var other = new GpuBigFloatArray(Context, 2, 4);
        using var negativeZero = new GpuBigFloat(new GpuBigUInt(Context, new uint[4]), 0, isNegative: true);
        using var zero = CreateFloat(0);
        vec.SetElement(0, negativeZero);
        vec.SetElement(1, zero);
        other.SetElement(0, zero);
        other.SetElement(1, negativeZero);
        var signsHost = new uint[2];
        Context.Provider.CopyDeviceToHost(signsHost, vec.Signs.DevicePointer, 2);
        Assert.Equal([1u, 0u], signsHost); // the negative zero is really stored with sign 1

        using var eqVector = vec.AsVectorizedEnumerable().IsEqualTo(other.AsVectorizedEnumerable());
        using var ltVector = vec.AsVectorizedEnumerable().IsLessThan(other.AsVectorizedEnumerable());
        using var eqScalar = vec.AsVectorizedEnumerable().IsEqualTo(negativeZero);
        var eqVectorHost = new uint[2];
        var ltVectorHost = new uint[2];
        var eqScalarHost = new uint[2];
        Context.Provider.CopyDeviceToHost(eqVectorHost, eqVector.Evaluate(Context).DevicePointer, 2);
        Context.Provider.CopyDeviceToHost(ltVectorHost, ltVector.Evaluate(Context).DevicePointer, 2);
        Context.Provider.CopyDeviceToHost(eqScalarHost, eqScalar.Evaluate(Context).DevicePointer, 2);

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
        using var vec = new GpuBigFloatArray(Context, length, words);
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

        using var gpuSum = vec.AsVectorizedEnumerable().Sum(Context);
        using var gpuMax = vec.AsVectorizedEnumerable().Max(Context);
        using var gpuMin = vec.AsVectorizedEnumerable().Min(Context);

        Assert.Equal(expectedSum, ToDouble(gpuSum), precision: 8);
        Assert.Equal(expectedMax, ToDouble(gpuMax), precision: 8);
        Assert.Equal(expectedMin, ToDouble(gpuMin), precision: 8);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void GpuBigFloatArray_VectorLinq_HeterogeneousScalarTypes_CalculateCorrectResults()
    {
        const int length = 3;
        const int words = 4;
        using var vec = new GpuBigFloatArray(Context, length, words);
        double[] vals = [10.0, 20.0, 30.0];
        for (var i = 0; i < length; i++)
        {
            using var f = CreateFloat((long)vals[i]);
            vec.SetElement(i, f);
        }

        using var uintScalar = new GpuBigUInt(Context, [5u, 0u, 0u, 0u]);
        using var intScalar = new GpuBigInt(new GpuBigUInt(Context, [4u, 0u, 0u, 0u]), isNegative: true);
        using var ufloatScalar = new GpuBigUFloat(new GpuBigUInt(Context, [3u, 0u, 0u, 0u]), 1); // 3 * 2^1 = 6.0

        using var addUIntExpr = vec.AsVectorizedEnumerable().SelectAdd(uintScalar);
        var addUIntRes = addUIntExpr.Evaluate(Context);
        using var mulIntExpr = vec.AsVectorizedEnumerable().SelectMultiply(intScalar);
        var mulIntRes = mulIntExpr.Evaluate(Context);
        using var subUFloatExpr = vec.AsVectorizedEnumerable().SelectSubtract(ufloatScalar);
        var subUFloatRes = subUFloatExpr.Evaluate(Context);

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
        using var vec = new GpuBigFloatArray(Context, length, words);
        using var expVec = new GpuBigFloatArray(Context, length, words);

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
        var sqrtRes = sqrtExpr.Evaluate(Context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(Math.Sqrt(vals[i]), ToDouble(sqrtRes.GetElement(i)), precision: 6);
        }

        // 2. SelectRsqrt
        using var rsqrtExpr = vec.AsVectorizedEnumerable().SelectRsqrt();
        var rsqrtRes = rsqrtExpr.Evaluate(Context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(1.0 / Math.Sqrt(vals[i]), ToDouble(rsqrtRes.GetElement(i)), precision: 6);
        }

        // 3. SelectReciprocal
        using var recipExpr = vec.AsVectorizedEnumerable().SelectReciprocal();
        var recipRes = recipExpr.Evaluate(Context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(1.0 / vals[i], ToDouble(recipRes.GetElement(i)), precision: 6);
        }

        // 4. SelectDivideByUInt32 (the quotient rounded toward zero to the element precision, so x / 2 is exact)
        using var divU32Expr = vec.AsVectorizedEnumerable().SelectDivideByUInt32(2u);
        var divU32Res = divU32Expr.Evaluate(Context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(vals[i] / 2.0, ToDouble(divU32Res.GetElement(i)), precision: 8);
        }

        // 5. SelectLog & SelectExp
        using var logExpr = vec.AsVectorizedEnumerable().SelectLog();
        var logRes = logExpr.Evaluate(Context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(Math.Log(vals[i]), ToDouble(logRes.GetElement(i)), precision: 5);
        }

        using var roundTripExpExpr = vec.AsVectorizedEnumerable().SelectLog().SelectExp();
        var roundTripExpRes = roundTripExpExpr.Evaluate(Context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(vals[i], ToDouble(roundTripExpRes.GetElement(i)), precision: 4);
        }

        // 6. SelectPow<TScalar> (with GpuBigUInt scalar 2)
        using var powScalar = new GpuBigUInt(Context, [2u, 0u, 0u, 0u]);
        using var powScalarExpr = vec.AsVectorizedEnumerable().SelectPow(powScalar);
        var powScalarRes = powScalarExpr.Evaluate(Context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(Math.Pow(vals[i], 2.0), ToDouble(powScalarRes.GetElement(i)), precision: 3);
        }

        // 7. ZipPow
        using var zipPowExpr = vec.AsVectorizedEnumerable().ZipPow(expVec.AsVectorizedEnumerable());
        var zipPowRes = zipPowExpr.Evaluate(Context);
        for (var i = 0; i < length; i++)
        {
            Assert.Equal(Math.Pow(vals[i], exps[i]), ToDouble(zipPowRes.GetElement(i)), precision: 4);
        }
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Evaluate_InvalidElementaryAndTranscendentalOperations_ThrowsExpectedExceptions()
    {
        using var zeroVec = new GpuBigFloatArray(Context, 2, 4);
        using var z0 = CreateFloat(0);
        using var z1 = CreateFloat(4);
        zeroVec.SetElement(0, z0);
        zeroVec.SetElement(1, z1);

        using var divZeroExpr = zeroVec.AsVectorizedEnumerable().SelectDivideByUInt32(0u);
        Assert.Throws<DivideByZeroException>(() => divZeroExpr.Evaluate(Context));
        using var recipZeroExpr = zeroVec.AsVectorizedEnumerable().SelectReciprocal();
        Assert.Throws<DivideByZeroException>(() => recipZeroExpr.Evaluate(Context));
        using var rsqrtZeroExpr = zeroVec.AsVectorizedEnumerable().SelectRsqrt();
        Assert.Throws<DivideByZeroException>(() => rsqrtZeroExpr.Evaluate(Context));

        using var negVec = new GpuBigFloatArray(Context, 2, 4);
        using var n0 = CreateFloat(-4);
        negVec.SetElement(0, n0);
        negVec.SetElement(1, z1);

        using var sqrtNegExpr = negVec.AsVectorizedEnumerable().SelectSqrt();
        Assert.Throws<InvalidOperationException>(() => sqrtNegExpr.Evaluate(Context));
        using var logNegExpr = negVec.AsVectorizedEnumerable().SelectLog();
        Assert.Throws<InvalidOperationException>(() => logNegExpr.Evaluate(Context));
        using var shortVec = new GpuBigFloatArray(Context, 1, 4);
        shortVec.SetElement(0, z1);
        using var mismatchedZipPow = zeroVec.AsVectorizedEnumerable().ZipPow(shortVec.AsVectorizedEnumerable());
        Assert.Throws<ArgumentException>(() => mismatchedZipPow.Evaluate(Context));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void Count_ProjectionThatThrows_ThrowsAsSystemLinq()
    {
        using var vec = new GpuBigFloatArray(Context, 3, 4);
        using var six = CreateFloat(6);
        using var minusFour = CreateFloat(-4);
        for (var i = 0; i < 3; i++)
        {
            vec.SetElement(i, i == 1 ? minusFour : six);
        }

        using var zero = CreateFloat(0);
        using var three = CreateFloat(3);
        double[] reference = [6, -4, 6];

        // Count(workspace) runs the projections it counts, as System.Linq: a zero divisor throws as Evaluate does.
        // mutant: GpuBigFloatVectorLinqExtensions.Count(workspace) without the evaluation of a MayThrow source
        // mutant: FloatVectorDivScalarExpression without `Divides => true`
        // decimal stands in for System.Linq: like GpuBigFloat, and unlike double, it has no infinity.
        Assert.Throws<DivideByZeroException>(() => reference.Select(static x => (decimal)x / 0m).Count());
        using (var divided = vec.AsVectorizedEnumerable().SelectDivide(zero))
        {
            Assert.Throws<DivideByZeroException>(() => divided.Count(Context));
        }

        // The square root of the negative element throws InvalidOperationException, as its Evaluate.
        // mutant: FloatVectorUnaryExpression.ElementMayThrow => false
        using (var rooted = vec.AsVectorizedEnumerable().SelectSqrt())
        {
            Assert.Throws<InvalidOperationException>(() => rooted.Evaluate(Context));
            Assert.Throws<InvalidOperationException>(() => rooted.Count(Context));
        }

        // A Take that leaves out the negative element counts without an error, as System.Linq evaluates only the
        // elements it counts.
        // mutant: FloatVectorTakeExpression.OperandRequest asking for the whole capacity of its input (throws)
        using (var firstRoot = vec.AsVectorizedEnumerable().SelectSqrt().Take(1))
        {
            Assert.Equal(reference.Take(1).Select(Math.Sqrt).Count(), firstRoot.Count(Context));
        }

        // A division by a non-zero scalar counts every element.
        using (var thirds = vec.AsVectorizedEnumerable().SelectDivide(three))
        {
            Assert.Equal(reference.Select(static x => x / 3).Count(), thirds.Count(Context));
        }

        // Count without a workspace cannot evaluate: a chain that can throw is refused instead of answered from the
        // length; a chain of plain operations keeps its length.
        // mutant: GpuBigFloatVectorLinqExtensions.Count() without the MayThrow check (returns 3)
        using (var divided = vec.AsVectorizedEnumerable().SelectDivide(three))
        {
            Assert.Throws<InvalidOperationException>(() => divided.Count());
        }

        using var plain = vec.AsVectorizedEnumerable().SelectAdd(three).SelectMultiply(three).Skip(1);
        Assert.Equal(reference.Select(static x => (x + 3) * 3).Skip(1).Count(), plain.Count());
        Assert.Equal(reference.Select(static x => (x + 3) * 3).Skip(1).Count(), plain.Count(Context));
    }
}
