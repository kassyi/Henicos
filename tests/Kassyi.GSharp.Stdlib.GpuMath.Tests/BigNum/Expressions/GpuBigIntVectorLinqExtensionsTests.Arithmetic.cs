using System;
using System.Numerics;
using System.Collections.Generic;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;
using Kassyi.GSharp.Tests.Infrastructure;
using Xunit;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

public partial class GpuBigIntVectorLinqExtensionsTests
{
    public static IEnumerable<object[]> ArithmeticData()
    {
        // Happy Path
        yield return new object[] { TestScenario.HappyPath, new BigInteger[] { 10, -5, 0, 100 }, new BigInteger(5), new BigInteger[] { 15, 0, 5, 105 }, new BigInteger[] { 5, -10, -5, 95 }, new BigInteger[] { 50, -25, 0, 500 } };
        // Boundary (Zeros)
        yield return new object[] { TestScenario.Boundary, new BigInteger[] { 0, 0, 0, 0 }, new BigInteger(0), new BigInteger[] { 0, 0, 0, 0 }, new BigInteger[] { 0, 0, 0, 0 }, new BigInteger[] { 0, 0, 0, 0 } };
        // Edge (Large numbers that exceed 1 word)
        var large = BigInteger.Parse("1000000000000000000000000000000");
        yield return new object[] { TestScenario.Boundary, new BigInteger[] { large, -large }, new BigInteger(10), new BigInteger[] { large + 10, -large + 10 }, new BigInteger[] { large - 10, -large - 10 }, new BigInteger[] { large * 10, -large * 10 } };
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ArithmeticData))]
    public void SelectAdd_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, BigInteger[] expectedAdd, BigInteger[] expectedSub, BigInteger[] expectedMul)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().SelectAdd(scalar);
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedAdd[i], hostResult[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ArithmeticData))]
    public void SelectSubtract_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, BigInteger[] expectedAdd, BigInteger[] expectedSub, BigInteger[] expectedMul)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().SelectSubtract(scalar);
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedSub[i], hostResult[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ArithmeticData))]
    public void SelectMultiply_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, BigInteger[] expectedAdd, BigInteger[] expectedSub, BigInteger[] expectedMul)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().SelectMultiply(scalar);
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedMul[i], hostResult[i]);
    }

    public static IEnumerable<object[]> ZipArithmeticData()
    {
        yield return new object[] { TestScenario.HappyPath, new BigInteger[] { 10, -5, 2, 100 }, new BigInteger[] { 2, 3, -4, 0 }, new BigInteger[] { 12, -2, -2, 100 }, new BigInteger[] { 8, -8, 6, 100 }, new BigInteger[] { 20, -15, -8, 0 } };
        // Boundary
        yield return new object[] { TestScenario.Boundary, new BigInteger[] { 0, 0 }, new BigInteger[] { 0, 0 }, new BigInteger[] { 0, 0 }, new BigInteger[] { 0, 0 }, new BigInteger[] { 0, 0 } };
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ZipArithmeticData))]
    public void ZipAdd_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input1, BigInteger[] input2, BigInteger[] expectedAdd, BigInteger[] expectedSub, BigInteger[] expectedMul)
    {
        using var array1 = CreateArray(input1);
        using var array2 = CreateArray(input2);
        var expr = array1.AsVectorizedEnumerable().ZipAdd(array2.AsVectorizedEnumerable());
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for(int i=0; i<input1.Length; i++) Assert.Equal(expectedAdd[i], hostResult[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ZipArithmeticData))]
    public void ZipSubtract_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input1, BigInteger[] input2, BigInteger[] expectedAdd, BigInteger[] expectedSub, BigInteger[] expectedMul)
    {
        using var array1 = CreateArray(input1);
        using var array2 = CreateArray(input2);
        var expr = array1.AsVectorizedEnumerable().ZipSubtract(array2.AsVectorizedEnumerable());
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for(int i=0; i<input1.Length; i++) Assert.Equal(expectedSub[i], hostResult[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ZipArithmeticData))]
    public void ZipMultiply_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input1, BigInteger[] input2, BigInteger[] expectedAdd, BigInteger[] expectedSub, BigInteger[] expectedMul)
    {
        using var array1 = CreateArray(input1);
        using var array2 = CreateArray(input2);
        var expr = array1.AsVectorizedEnumerable().ZipMultiply(array2.AsVectorizedEnumerable());
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for(int i=0; i<input1.Length; i++) Assert.Equal(expectedMul[i], hostResult[i]);
    }
}
