using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;
using Kassyi.GSharp.Tests.Helpers;

namespace Kassyi.GSharp.Tests.Stdlib;

public class GBI35_GpuBigIntegerTests : TranspilerTestBase
{
    public GBI35_GpuBigIntegerTests()
    {
        SkipIfCpuDialect("This test interacts directly with CUDA/Runtime components and cannot run on CPU.");
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "HappyPath")]
    public void Test_GBI35_ConstructorAndProperties_StoreCorrectValues()
    {
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

    [SkippableTheory]
    [Trait("FeatureId", "GBI-35")]
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
    // New Edge Cases for Zero and adjacent magnitudes
    [InlineData(5, false, 0, false, 1)]   // +5 > 0
    [InlineData(5, true, 0, false, -1)]   // -5 < 0
    [InlineData(uint.MaxValue, false, uint.MaxValue - 1, false, 1)] // Max > Max - 1
    [InlineData(uint.MaxValue - 1, false, uint.MaxValue, false, -1)] // Max - 1 < Max
    [InlineData(uint.MaxValue, true, uint.MaxValue - 1, true, -1)] // -Max < -(Max - 1)
    [InlineData(0xAAAAAAAA, false, 0x55555555, false, 1)] // Alternating bits High > Low
    public void Test_GBI35_ComparisonAndRelationalOperators_BehaveCorrectly(
        uint valA, bool negA, uint valB, bool negB, int expectedCmp)
    {
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

    [SkippableTheory]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "HappyPath")]
    [InlineData(false, false, false)] // (+A) * (+B) = +Result
    [InlineData(false, true, true)]   // (+A) * (-B) = -Result
    [InlineData(true, false, true)]   // (-A) * (+B) = -Result
    [InlineData(true, true, false)]   // (-A) * (-B) = +Result
    public void Test_GBI35_SignedMultiplicationAndDivisionExpressions_SignRules(bool negA, bool negB, bool expectedNeg)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA = GpuUIntBigInteger.FromBigInteger(context, 100, 4);
        using var uB = GpuUIntBigInteger.FromBigInteger(context, 20, 4);

        var a = new GpuBigInteger(uA, negA);
        var b = new GpuBigInteger(uB, negB);

        GpuBigIntExpr mulExpr = a * b;
        GpuBigIntExpr divExpr = a / b;

        using var destMul = new GpuUIntBigInteger(context, 8);
        using var evalMul = mulExpr.Evaluate(destMul);
        Assert.Equal(expectedNeg, evalMul.IsNegative);

        using var destDiv = new GpuUIntBigInteger(context, 4);
        using var evalDiv = divExpr.Evaluate(destDiv);
        Assert.Equal(expectedNeg, evalDiv.IsNegative);
    }

    [SkippableTheory]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "HappyPath")]
    [InlineData(false, false, false)] // (+A) + (+B) = +Result
    [InlineData(true, true, true)]    // (-A) + (-B) = -Result
    public void Test_GBI35_SignedAdditionExpression_SameSigns_Succeeds(bool negA, bool negB, bool expectedNeg)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA = GpuUIntBigInteger.FromBigInteger(context, 10, 4);
        using var uB = GpuUIntBigInteger.FromBigInteger(context, 5, 4);

        var a = new GpuBigInteger(uA, negA);
        var b = new GpuBigInteger(uB, negB);

        GpuBigIntExpr addExpr = a + b;

        using var dest = new GpuUIntBigInteger(context, 4);
        using var evalAdd = addExpr.Evaluate(dest);
        Assert.Equal(expectedNeg, evalAdd.IsNegative);
    }

    [SkippableTheory]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "HappyPath")]
    [InlineData(10, false, 5, true, 5, false)]  // (+10) + (-5) = +5
    [InlineData(5, false, 10, true, 5, true)]   // (+5) + (-10) = -5
    [InlineData(10, true, 5, false, 5, true)]   // (-10) + (+5) = -5
    [InlineData(5, true, 10, false, 5, false)]  // (-5) + (+10) = +5
    [InlineData(10, false, 10, true, 0, false)] // (+10) + (-10) = 0
    [InlineData(uint.MaxValue, false, 1, true, uint.MaxValue - 1, false)] // Max + (-1) = Max - 1
    [InlineData(1, false, uint.MaxValue, true, uint.MaxValue - 1, true)]  // 1 + (-Max) = -(Max - 1)
    [InlineData(0, false, 0, true, 0, false)] // 0 + (-0) = 0
    [InlineData(0xAAAAAAAA, false, 0x55555555, true, 0x55555555, false)] // 0xAAAAAAAA + (-0x55555555) = 0x55555555
    [InlineData(0x55555555, false, 0xAAAAAAAA, true, 0x55555555, true)]  // 0x55555555 + (-0xAAAAAAAA) = -0x55555555
    public void Test_GBI35_SignedAdditionExpression_DifferentSigns_Succeeds(uint valA, bool negA, uint valB, bool negB, uint expectedVal, bool expectedNeg)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA = valA == 0 ? new GpuUIntBigInteger(context, 4) : GpuUIntBigInteger.FromBigInteger(context, valA, 4);
        using var uB = valB == 0 ? new GpuUIntBigInteger(context, 4) : GpuUIntBigInteger.FromBigInteger(context, valB, 4);

        var a = new GpuBigInteger(uA, negA);
        var b = new GpuBigInteger(uB, negB);

        GpuBigIntExpr addExpr = a + b;

        using var dest = new GpuUIntBigInteger(context, 4);
        using var evalAdd = addExpr.Evaluate(dest);
        
        Assert.Equal(expectedNeg, evalAdd.IsNegative);
        
        // Also verify the absolute magnitude
        using var uExpected = expectedVal == 0 ? new GpuUIntBigInteger(context, 4) : GpuUIntBigInteger.FromBigInteger(context, expectedVal, 4);
        Assert.Equal(0, evalAdd.Value.CompareTo(uExpected));
    }

    [SkippableTheory]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "HappyPath")]
    [InlineData(false, true, false)] // (+A) - (-B) = (+A) + (+B) = +Result
    [InlineData(true, false, true)]  // (-A) - (+B) = (-A) + (-B) = -Result
    public void Test_GBI35_SignedSubtractionExpression_DifferentSigns_Succeeds(bool negA, bool negB, bool expectedNeg)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA = GpuUIntBigInteger.FromBigInteger(context, 10, 4);
        using var uB = GpuUIntBigInteger.FromBigInteger(context, 5, 4);

        var a = new GpuBigInteger(uA, negA);
        var b = new GpuBigInteger(uB, negB);

        GpuBigIntExpr subExpr = a - b;

        using var dest = new GpuUIntBigInteger(context, 4);
        using var evalSub = subExpr.Evaluate(dest);
        Assert.Equal(expectedNeg, evalSub.IsNegative);
    }

    [SkippableTheory]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "HappyPath")]
    [InlineData(10, false, 5, false, 5, false)]  // (+10) - (+5) = +5
    [InlineData(5, false, 10, false, 5, true)]   // (+5) - (+10) = -5
    [InlineData(10, true, 5, true, 5, true)]     // (-10) - (-5) = -5
    [InlineData(5, true, 10, true, 5, false)]    // (-5) - (-10) = +5
    [InlineData(10, false, 10, false, 0, false)] // (+10) - (+10) = 0
    [InlineData(uint.MaxValue, false, 1, false, uint.MaxValue - 1, false)]
    [InlineData(1, false, uint.MaxValue, false, uint.MaxValue - 1, true)]
    [InlineData(0, false, 0, false, 0, false)] // 0 - 0 = 0
    [InlineData(0, true, 0, true, 0, false)]   // (-0) - (-0) = 0
    [InlineData(0xAAAAAAAA, false, 0x55555555, false, 0x55555555, false)]
    [InlineData(0x55555555, false, 0xAAAAAAAA, false, 0x55555555, true)]
    public void Test_GBI35_SignedSubtractionExpression_SameSigns_Succeeds(uint valA, bool negA, uint valB, bool negB, uint expectedVal, bool expectedNeg)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA = valA == 0 ? new GpuUIntBigInteger(context, 4) : GpuUIntBigInteger.FromBigInteger(context, valA, 4);
        using var uB = valB == 0 ? new GpuUIntBigInteger(context, 4) : GpuUIntBigInteger.FromBigInteger(context, valB, 4);

        var a = new GpuBigInteger(uA, negA);
        var b = new GpuBigInteger(uB, negB);

        GpuBigIntExpr subExpr = a - b;

        using var dest = new GpuUIntBigInteger(context, 4);
        using var evalSub = subExpr.Evaluate(dest);
        
        Assert.Equal(expectedNeg, evalSub.IsNegative);

        using var uExpected = expectedVal == 0 ? new GpuUIntBigInteger(context, 4) : GpuUIntBigInteger.FromBigInteger(context, expectedVal, 4);
        Assert.Equal(0, evalSub.Value.CompareTo(uExpected));
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "HappyPath")]
    public void Test_GBI35_UnaryOperators_NegationAndBitwiseNot()
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA = new GpuUIntBigInteger(context, 4);
        var a = new GpuBigInteger(uA, isNegative: false);

        GpuBigIntExpr exprA = a;
        GpuBigIntExpr negExpr = -exprA;
        GpuBigIntExpr notExpr = ~exprA;

        using var destNeg = new GpuUIntBigInteger(context, 4);
        using var evalNeg = negExpr.Evaluate(destNeg);
        Assert.True(evalNeg.IsNegative);

        using var destNot = new GpuUIntBigInteger(context, 4);
        using var evalNot = notExpr.Evaluate(destNot);
        Assert.False(evalNot.IsNegative);
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-35")]
    [Trait("Category", "SadPath")]
    public void Test_GBI35_Evaluate_SadPaths_ThrowsAndReleasesBuffers()
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var uA_4 = GpuUIntBigInteger.FromBigInteger(context, 10, 4);
        using var uB_4 = GpuUIntBigInteger.FromBigInteger(context, 0, 4); // For division by zero
        using var uC_8 = GpuUIntBigInteger.FromBigInteger(context, 20, 8); // Unequal width

        var a = new GpuBigInteger(uA_4, false);
        var b = new GpuBigInteger(uB_4, false);
        var c = new GpuBigInteger(uC_8, false);

        GpuBigIntExpr exprAddUnequal = a + c;
        
        // 1. Unequal operand widths
        Assert.Throws<ArgumentException>(() => exprAddUnequal.Evaluate(context));
        
        // 2. Incompatible destination widths
        GpuBigIntExpr exprAdd = a + b;
        using var invalidDest = new GpuUIntBigInteger(context, 8); // Should be 4
        Assert.Throws<ArgumentException>(() => exprAdd.Evaluate(invalidDest));

        // 3. Division by zero (verify temps are reused via loop)
        GpuBigIntExpr exprDivZero = a / b;
        using var destDiv = new GpuUIntBigInteger(context, 4);
        
        for (int i = 0; i < 100; i++)
        {
            Assert.Throws<DivideByZeroException>(() => exprDivZero.Evaluate(destDiv));
        }
        
        // 4. Multiplication without explicit destination (allocates sum of widths correctly)
        GpuBigIntExpr exprMul = a * b; // 4 + 4 = 8
        using var mulResult = exprMul.Evaluate(context); // Rents temporary buffer
        Assert.Equal(8, mulResult.Value.NumWords);
        
        using var uExpected = new GpuUIntBigInteger(context, 8);
        Assert.Equal(0, mulResult.Value.CompareTo(uExpected)); // Is zero
    }
}
