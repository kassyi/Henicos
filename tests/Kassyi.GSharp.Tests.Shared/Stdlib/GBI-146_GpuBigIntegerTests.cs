using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

namespace Kassyi.GSharp.Tests.Stdlib;

public class GBI146_GpuBigIntegerTests
{
    [Fact]
    [Trait("FeatureId", "GBI-146")]
    [Trait("Category", "HappyPath")]
    public void Test_GBI146_ConstructorAndProperties_StoreCorrectValues()
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uintVal = new GpuUIntBigInteger(context, 4);
        var posBigInt = new GpuBigInteger(uintVal, isNegative: false);
        var negBigInt = new GpuBigInteger(uintVal, isNegative: true);

        Assert.False(posBigInt.IsNegative);
        Assert.True(negBigInt.IsNegative);
        Assert.Same(uintVal, posBigInt.Value);
        Assert.Same(uintVal, negBigInt.Value);
    }

    [Theory]
    [Trait("FeatureId", "GBI-146")]
    [Trait("Category", "HappyPath")]
    [InlineData(0, false, 0, false, 0)]   // 0 == 0
    [InlineData(10, false, 5, false, 1)]  // +10 > +5
    [InlineData(5, false, 10, false, -1)] // +5 < +10
    [InlineData(10, true, 5, true, -1)]   // -10 < -5
    [InlineData(5, true, 10, true, 1)]    // -5 > -10
    [InlineData(10, false, 10, true, 1)]  // +10 > -10
    [InlineData(10, true, 10, false, -1)] // -10 < +10
    [InlineData(0, false, 5, true, 1)]    // 0 > -5
    [InlineData(0, false, 5, false, -1)]  // 0 < +5
    public void Test_GBI146_ComparisonAndRelationalOperators_BehaveCorrectly(
        uint valA, bool negA, uint valB, bool negB, int expectedCmp)
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA = valA == 0 ? new GpuUIntBigInteger(context, 4) : GpuUIntBigInteger.FromBigInteger(context, valA, 4);
        using var uB = valB == 0 ? new GpuUIntBigInteger(context, 4) : GpuUIntBigInteger.FromBigInteger(context, valB, 4);

        var a = new GpuBigInteger(uA, negA);
        var b = new GpuBigInteger(uB, negB);

        Assert.Equal(expectedCmp, a.CompareTo(b));

        if (expectedCmp == 0)
        {
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.False(a < b);
            Assert.False(a > b);
            Assert.True(a <= b);
            Assert.True(a >= b);
            Assert.True(a.Equals(b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }
        else if (expectedCmp > 0)
        {
            Assert.False(a == b);
            Assert.True(a != b);
            Assert.False(a < b);
            Assert.True(a > b);
            Assert.False(a <= b);
            Assert.True(a >= b);
            Assert.False(a.Equals(b));
        }
        else
        {
            Assert.False(a == b);
            Assert.True(a != b);
            Assert.True(a < b);
            Assert.False(a > b);
            Assert.True(a <= b);
            Assert.False(a >= b);
            Assert.False(a.Equals(b));
        }
    }

    [Theory]
    [Trait("FeatureId", "GBI-146")]
    [Trait("Category", "HappyPath")]
    [InlineData(false, false, false)] // (+A) * (+B) = +Result
    [InlineData(false, true, true)]   // (+A) * (-B) = -Result
    [InlineData(true, false, true)]   // (-A) * (+B) = -Result
    [InlineData(true, true, false)]   // (-A) * (-B) = +Result
    public void Test_GBI146_SignedMultiplicationAndDivisionExpressions_SignRules(bool negA, bool negB, bool expectedNeg)
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA = GpuUIntBigInteger.FromBigInteger(context, 100, 4);
        using var uB = GpuUIntBigInteger.FromBigInteger(context, 20, 4);

        var a = new GpuBigInteger(uA, negA);
        var b = new GpuBigInteger(uB, negB);

        GpuBigIntExpr mulExpr = a * b;
        Assert.Equal(expectedNeg, mulExpr.IsNegative);

        GpuBigIntExpr divExpr = a / b;
        Assert.Equal(expectedNeg, divExpr.IsNegative);

        using var dest = new GpuUIntBigInteger(context, 8);
        using var evalMul = mulExpr.Evaluate(dest);
        Assert.Equal(expectedNeg, evalMul.IsNegative);

        using var evalDiv = divExpr.Evaluate(dest);
        Assert.Equal(expectedNeg, evalDiv.IsNegative);
    }

    [Theory]
    [Trait("FeatureId", "GBI-146")]
    [Trait("Category", "HappyPath")]
    [InlineData(false, false, false)] // (+A) + (+B) = +Result
    [InlineData(true, true, true)]    // (-A) + (-B) = -Result
    public void Test_GBI146_SignedAdditionExpression_SameSigns_Succeeds(bool negA, bool negB, bool expectedNeg)
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA = GpuUIntBigInteger.FromBigInteger(context, 10, 4);
        using var uB = GpuUIntBigInteger.FromBigInteger(context, 5, 4);

        var a = new GpuBigInteger(uA, negA);
        var b = new GpuBigInteger(uB, negB);

        GpuBigIntExpr addExpr = a + b;
        Assert.Equal(expectedNeg, addExpr.IsNegative);

        using var dest = new GpuUIntBigInteger(context, 4);
        using var evalAdd = addExpr.Evaluate(dest);
        Assert.Equal(expectedNeg, evalAdd.IsNegative);
    }

    [Theory]
    [Trait("FeatureId", "GBI-146")]
    [Trait("Category", "SadPath")]
    [InlineData(false, true)] // (+A) + (-B) => Not supported in lazy AST
    [InlineData(true, false)] // (-A) + (+B) => Not supported in lazy AST
    public void Test_GBI146_SignedAdditionExpression_DifferentSigns_ThrowsNotSupportedException(bool negA, bool negB)
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA = new GpuUIntBigInteger(context, 4);
        using var uB = new GpuUIntBigInteger(context, 4);

        var a = new GpuBigInteger(uA, negA);
        var b = new GpuBigInteger(uB, negB);

        Assert.Throws<NotSupportedException>(() => _ = a + b);
    }

    [Theory]
    [Trait("FeatureId", "GBI-146")]
    [Trait("Category", "HappyPath")]
    [InlineData(false, true, false)] // (+A) - (-B) = (+A) + (+B) = +Result
    [InlineData(true, false, true)]  // (-A) - (+B) = (-A) + (-B) = -Result
    public void Test_GBI146_SignedSubtractionExpression_DifferentSigns_Succeeds(bool negA, bool negB, bool expectedNeg)
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA = GpuUIntBigInteger.FromBigInteger(context, 10, 4);
        using var uB = GpuUIntBigInteger.FromBigInteger(context, 5, 4);

        var a = new GpuBigInteger(uA, negA);
        var b = new GpuBigInteger(uB, negB);

        GpuBigIntExpr subExpr = a - b;
        Assert.Equal(expectedNeg, subExpr.IsNegative);
    }

    [Theory]
    [Trait("FeatureId", "GBI-146")]
    [Trait("Category", "SadPath")]
    [InlineData(false, false)] // (+A) - (+B) => Not supported in lazy AST
    [InlineData(true, true)]   // (-A) - (-B) => Not supported in lazy AST
    public void Test_GBI146_SignedSubtractionExpression_SameSigns_ThrowsNotSupportedException(bool negA, bool negB)
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA = new GpuUIntBigInteger(context, 4);
        using var uB = new GpuUIntBigInteger(context, 4);

        var a = new GpuBigInteger(uA, negA);
        var b = new GpuBigInteger(uB, negB);

        Assert.Throws<NotSupportedException>(() => _ = a - b);
    }

    [Fact]
    [Trait("FeatureId", "GBI-146")]
    [Trait("Category", "HappyPath")]
    public void Test_GBI146_UnaryOperators_NegationAndBitwiseNot()
    {
        if (!GpuKernelLoader.IsDriverAvailable) return;
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA = new GpuUIntBigInteger(context, 4);
        var a = new GpuBigInteger(uA, isNegative: false);

        GpuBigIntExpr exprA = a;
        GpuBigIntExpr negExpr = -exprA;
        GpuBigIntExpr notExpr = ~exprA;

        Assert.True(negExpr.IsNegative);
        Assert.False(notExpr.IsNegative);
    }
}
