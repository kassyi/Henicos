#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;
using static Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions.GpuBigFloatTestHelpers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

[TestTarget(ExecutionTarget.GpuExecution)]
public class GpuBigFloatStackLinqTests : GpuBigFloatWorkspaceTestBase
{
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
    public void StackEnumerable_GenericUnification_GpuBigUInt_And_GpuBigUFloat_CalculateCorrectResults()
    {
        using var u0 = new GpuBigUInt(Context, [10u, 0u, 0u, 0u]);
        using var u1 = new GpuBigUInt(Context, [20u, 0u, 0u, 0u]);
        using var u2 = new GpuBigUInt(Context, [30u, 0u, 0u, 0u]);
        using var u3 = new GpuBigUInt(Context, [40u, 0u, 0u, 0u]);
        using var uScalar = new GpuBigUInt(Context, [5u, 0u, 0u, 0u]);

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
        using var uf0 = new GpuBigUFloat(new GpuBigUInt(Context, [12u, 0u, 0u, 0u]), 0);
        using var uf1 = new GpuBigUFloat(new GpuBigUInt(Context, [24u, 0u, 0u, 0u]), 0);
        using var uf2 = new GpuBigUFloat(new GpuBigUInt(Context, [36u, 0u, 0u, 0u]), 0);
        using var ufScalar = new GpuBigUFloat(new GpuBigUInt(Context, [3u, 0u, 0u, 0u]), 0);

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

    // Each Select / Zip overload applies the operation it names, on the parallel and the sequential enumerable.
    // a = [6, 9], scalar 3, b = [3, 2]: every sum differs from the sum of the swapped operation (+ <-> -, * <-> /).
    // a >= b element-wise, so the unsigned subtraction stays non-negative (#629 is not involved).
    private void AssertSelectZipOverloads<T>()
        where T : struct, IGpuBigNumber<T>
    {
        var a = new[] { CreateNumber<T>(6), CreateNumber<T>(9) };
        var b = new[] { CreateNumber<T>(3), CreateNumber<T>(2) };
        var scalar = CreateNumber<T>(3);

        // mutant: GpuBigNumberStackLinqExtensions.cs:47 / 59 / 67 / 75 (parallel Select*), and the generic operators
        // GpuBigFloat.cs:307 / GpuBigUFloat.cs:355 `left - right` -> `left + right`, GpuBigUFloat.cs:359 `*` -> `/`
        AssertExactSum("parallel SelectAdd", 21, 1, a.AsStackEnumerable().AsParallel().SelectAdd(scalar).Sum());
        AssertExactSum(
            "parallel SelectSubtract",
            9,
            1,
            a.AsStackEnumerable().AsParallel().SelectSubtract(scalar).Sum());
        AssertExactSum(
            "parallel SelectMultiply",
            45,
            1,
            a.AsStackEnumerable().AsParallel().SelectMultiply(scalar).Sum());
        AssertExactSum("parallel SelectDivide", 5, 1, a.AsStackEnumerable().AsParallel().SelectDivide(scalar).Sum());

        // mutant: GpuBigNumberStackLinqExtensions.cs:178 / 191 / 200 / 209 (sequential Select*)
        AssertExactSum("SelectAdd", 21, 1, a.AsStackEnumerable().SelectAdd(scalar).Sum());
        AssertExactSum("SelectSubtract", 9, 1, a.AsStackEnumerable().SelectSubtract(scalar).Sum());
        AssertExactSum("SelectMultiply", 45, 1, a.AsStackEnumerable().SelectMultiply(scalar).Sum());
        AssertExactSum("SelectDivide", 5, 1, a.AsStackEnumerable().SelectDivide(scalar).Sum());

        // mutant: GpuBigNumberStackLinqExtensions.cs:282 / 305 / 324 / 343 (parallel Zip*)
        AssertExactSum(
            "parallel ZipAdd",
            20,
            1,
            a.AsStackEnumerable().AsParallel().ZipAdd(b.AsStackEnumerable().AsParallel()).Sum());
        AssertExactSum(
            "parallel ZipSubtract",
            10,
            1,
            a.AsStackEnumerable().AsParallel().ZipSubtract(b.AsStackEnumerable().AsParallel()).Sum());
        AssertExactSum(
            "parallel ZipMultiply",
            36,
            1,
            a.AsStackEnumerable().AsParallel().ZipMultiply(b.AsStackEnumerable().AsParallel()).Sum());
        AssertExactSum(
            "parallel ZipDivide",
            13,
            2,
            a.AsStackEnumerable().AsParallel().ZipDivide(b.AsStackEnumerable().AsParallel()).Sum());

        // mutant: GpuBigNumberStackLinqExtensions.cs:404 / 427 / 446 / 465 (sequential Zip*)
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
    // mutant: GpuBigFloatExpressionVisitor.cs:111 `if (!left.CanBeNegative && res.IsNegative)` -> `if (false)`
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
        Assert.Throws<OverflowException>(
            () => a.AsStackEnumerable().AsParallel().ZipSubtract(b.AsStackEnumerable().AsParallel()).Sum());
        Assert.Throws<OverflowException>(() => a.AsStackEnumerable().ZipSubtract(b.AsStackEnumerable()).Sum());
    }
}
