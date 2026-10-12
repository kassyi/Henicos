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
    private uint[] ToHostBoolArray(IGpuBoolVectorExpression expr)
    {
        var array = expr.Evaluate(_context);
        var hostResult = new uint[array.Length];
        _context.Provider.CopyDeviceToHost(hostResult, array.DevicePointer, array.Length);
        array.Dispose();
        return hostResult;
    }

    public static IEnumerable<object[]> BooleanComparisonData()
    {
        yield return new object[] { TestScenario.HappyPath, new BigInteger[] { 10, -5, 100, 50 }, new BigInteger(50), 
            new uint[] { 0, 0, 0, 1 }, // EQ
            new uint[] { 1, 1, 1, 0 }, // NEQ
            new uint[] { 0, 0, 1, 0 }, // GT
            new uint[] { 0, 0, 1, 1 }, // GTE
            new uint[] { 1, 1, 0, 0 }, // LT
            new uint[] { 1, 1, 0, 1 }  // LTE
        };

        yield return new object[] { TestScenario.Boundary, new BigInteger[] { 0, 0, 0, 0 }, new BigInteger(0), 
            new uint[] { 1, 1, 1, 1 }, // EQ
            new uint[] { 0, 0, 0, 0 }, // NEQ
            new uint[] { 0, 0, 0, 0 }, // GT
            new uint[] { 1, 1, 1, 1 }, // GTE
            new uint[] { 0, 0, 0, 0 }, // LT
            new uint[] { 1, 1, 1, 1 }  // LTE
        };
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(BooleanComparisonData))]
    public void IsEqualTo_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, uint[] expectedEq, uint[] expectedNeq, uint[] expectedGt, uint[] expectedGte, uint[] expectedLt, uint[] expectedLte)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().IsEqualTo(scalar);
        var result = ToHostBoolArray(expr);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedEq[i], result[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(BooleanComparisonData))]
    public void IsNotEqualTo_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, uint[] expectedEq, uint[] expectedNeq, uint[] expectedGt, uint[] expectedGte, uint[] expectedLt, uint[] expectedLte)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().IsNotEqualTo(scalar);
        var result = ToHostBoolArray(expr);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedNeq[i], result[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(BooleanComparisonData))]
    public void IsGreaterThan_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, uint[] expectedEq, uint[] expectedNeq, uint[] expectedGt, uint[] expectedGte, uint[] expectedLt, uint[] expectedLte)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().IsGreaterThan(scalar);
        var result = ToHostBoolArray(expr);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedGt[i], result[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(BooleanComparisonData))]
    public void IsGreaterThanOrEqualTo_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, uint[] expectedEq, uint[] expectedNeq, uint[] expectedGt, uint[] expectedGte, uint[] expectedLt, uint[] expectedLte)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().IsGreaterThanOrEqualTo(scalar);
        var result = ToHostBoolArray(expr);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedGte[i], result[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(BooleanComparisonData))]
    public void IsLessThan_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, uint[] expectedEq, uint[] expectedNeq, uint[] expectedGt, uint[] expectedGte, uint[] expectedLt, uint[] expectedLte)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().IsLessThan(scalar);
        var result = ToHostBoolArray(expr);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedLt[i], result[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(BooleanComparisonData))]
    public void IsLessThanOrEqualTo_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal, uint[] expectedEq, uint[] expectedNeq, uint[] expectedGt, uint[] expectedGte, uint[] expectedLt, uint[] expectedLte)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().IsLessThanOrEqualTo(scalar);
        var result = ToHostBoolArray(expr);
        for(int i=0; i<input.Length; i++) Assert.Equal(expectedLte[i], result[i]);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void LogicalAnd_EvaluatesCorrectly()
    {
        using var array = CreateArray(new BigInteger[] { 10, 50, 100, 200 });
        var lower = CreateScalar(20);
        var upper = CreateScalar(150);
        var expr = array.AsVectorizedEnumerable().IsGreaterThan(lower).And(array.AsVectorizedEnumerable().IsLessThan(upper));
        var result = ToHostBoolArray(expr);
        Assert.Equal(new uint[] { 0, 1, 1, 0 }, result);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void LogicalOr_EvaluatesCorrectly()
    {
        using var array = CreateArray(new BigInteger[] { 10, 50, 100, 200 });
        var lower = CreateScalar(20);
        var upper = CreateScalar(150);
        var expr = array.AsVectorizedEnumerable().IsLessThan(lower).Or(array.AsVectorizedEnumerable().IsGreaterThan(upper));
        var result = ToHostBoolArray(expr);
        Assert.Equal(new uint[] { 1, 0, 0, 1 }, result);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void LogicalNot_EvaluatesCorrectly()
    {
        using var array = CreateArray(new BigInteger[] { 10, 50, 100, 200 });
        var threshold = CreateScalar(60);
        var expr = array.AsVectorizedEnumerable().IsGreaterThan(threshold).Not();
        var result = ToHostBoolArray(expr);
        Assert.Equal(new uint[] { 1, 1, 0, 0 }, result); // Effectively LTE
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void BooleanAny_EvaluatesCorrectly()
    {
        using var array = CreateArray(new BigInteger[] { 10, 50, 100, 200 });
        var threshold = CreateScalar(150);
        var expr = array.AsVectorizedEnumerable().IsGreaterThan(threshold).Any();
        Assert.True(expr.Evaluate(_context)); // 200 > 150
        
        var threshold2 = CreateScalar(300);
        var expr2 = array.AsVectorizedEnumerable().IsGreaterThan(threshold2).Any();
        Assert.False(expr2.Evaluate(_context));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    public void BooleanAll_EvaluatesCorrectly()
    {
        using var array = CreateArray(new BigInteger[] { 10, 50, 100, 200 });
        var threshold = CreateScalar(0);
        var expr = array.AsVectorizedEnumerable().IsGreaterThan(threshold).All();
        Assert.True(expr.Evaluate(_context)); // All > 0
        
        var threshold2 = CreateScalar(20);
        var expr2 = array.AsVectorizedEnumerable().IsGreaterThan(threshold2).All();
        Assert.False(expr2.Evaluate(_context)); // 10 is not > 20
    }
}
