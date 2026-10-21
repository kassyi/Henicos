
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

public partial class GpuBigIntVectorLinqExtensionsTests
{
    public static IEnumerable<object[]> ArithmeticData()
    {
        // Happy Path
        yield return new object[]
        {
            TestScenario.HappyPath, new BigInteger[] { 10, -5, 0, 100 }, new BigInteger(5),
            new BigInteger[] { 15, 0, 5, 105 }, new BigInteger[] { 5, -10, -5, 95 },
            new BigInteger[] { 50, -25, 0, 500 }
        };
        // Boundary (Zeros)
        yield return new object[]
        {
            TestScenario.Boundary, new BigInteger[] { 0, 0, 0, 0 }, new BigInteger(0), new BigInteger[] { 0, 0, 0, 0 },
            new BigInteger[] { 0, 0, 0, 0 }, new BigInteger[] { 0, 0, 0, 0 }
        };
        // Edge (Large numbers that exceed 1 word)
        var large = BigInteger.Parse("1000000000000000000000000000000");
        yield return new object[]
        {
            TestScenario.Boundary, new BigInteger[] { large, -large }, new BigInteger(10),
            new BigInteger[] { large + 10, -large + 10 }, new BigInteger[] { large - 10, -large - 10 },
            new BigInteger[] { large * 10, -large * 10 }
        };
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ArithmeticData))]
    public void SelectAdd_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal,
        BigInteger[] expectedAdd, BigInteger[] expectedSub, BigInteger[] expectedMul)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().SelectAdd(scalar);
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for (int i = 0; i < input.Length; i++) Assert.Equal(expectedAdd[i], hostResult[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ArithmeticData))]
    public void SelectSubtract_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal,
        BigInteger[] expectedAdd, BigInteger[] expectedSub, BigInteger[] expectedMul)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().SelectSubtract(scalar);
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for (int i = 0; i < input.Length; i++) Assert.Equal(expectedSub[i], hostResult[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ArithmeticData))]
    public void SelectMultiply_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal,
        BigInteger[] expectedAdd, BigInteger[] expectedSub, BigInteger[] expectedMul)
    {
        using var array = CreateArray(input);
        var scalar = CreateScalar(scalarVal);
        var expr = array.AsVectorizedEnumerable().SelectMultiply(scalar);
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for (int i = 0; i < input.Length; i++) Assert.Equal(expectedMul[i], hostResult[i]);
    }

    public static IEnumerable<object[]> ZipArithmeticData()
    {
        yield return new object[]
        {
            TestScenario.HappyPath, new BigInteger[] { 10, -5, 2, 100 }, new BigInteger[] { 2, 3, -4, 0 },
            new BigInteger[] { 12, -2, -2, 100 }, new BigInteger[] { 8, -8, 6, 100 },
            new BigInteger[] { 20, -15, -8, 0 }
        };
        // Boundary
        yield return new object[]
        {
            TestScenario.Boundary, new BigInteger[] { 0, 0 }, new BigInteger[] { 0, 0 }, new BigInteger[] { 0, 0 },
            new BigInteger[] { 0, 0 }, new BigInteger[] { 0, 0 }
        };
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ZipArithmeticData))]
    public void ZipAdd_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input1, BigInteger[] input2,
        BigInteger[] expectedAdd, BigInteger[] expectedSub, BigInteger[] expectedMul)
    {
        using var array1 = CreateArray(input1);
        using var array2 = CreateArray(input2);
        var expr = array1.AsVectorizedEnumerable().ZipAdd(array2.AsVectorizedEnumerable());
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for (int i = 0; i < input1.Length; i++) Assert.Equal(expectedAdd[i], hostResult[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ZipArithmeticData))]
    public void ZipSubtract_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input1, BigInteger[] input2,
        BigInteger[] expectedAdd, BigInteger[] expectedSub, BigInteger[] expectedMul)
    {
        using var array1 = CreateArray(input1);
        using var array2 = CreateArray(input2);
        var expr = array1.AsVectorizedEnumerable().ZipSubtract(array2.AsVectorizedEnumerable());
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for (int i = 0; i < input1.Length; i++) Assert.Equal(expectedSub[i], hostResult[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ZipArithmeticData))]
    public void ZipMultiply_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input1, BigInteger[] input2,
        BigInteger[] expectedAdd, BigInteger[] expectedSub, BigInteger[] expectedMul)
    {
        using var array1 = CreateArray(input1);
        using var array2 = CreateArray(input2);
        var expr = array1.AsVectorizedEnumerable().ZipMultiply(array2.AsVectorizedEnumerable());
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for (int i = 0; i < input1.Length; i++) Assert.Equal(expectedMul[i], hostResult[i]);
    }

    public static IEnumerable<object[]> DivisionData()
    {
        // Happy Path (mixed signs, exact and non-exact division)
        yield return new object[]
        {
            TestScenario.HappyPath,
            new BigInteger[] { 100, -47, 0, 15 },
            new BigInteger(7),
            new BigInteger[] { 100 / 7, -47 / 7, 0, 15 / 7 },
            new BigInteger[] { 100 % 7, -47 % 7, 0, 15 % 7 }
        };
        // Negative scalar divisor
        yield return new object[]
        {
            TestScenario.HappyPath,
            new BigInteger[] { 100, -47, 0, 15 },
            new BigInteger(-7),
            new BigInteger[] { 100 / -7, -47 / -7, 0, 15 / -7 },
            new BigInteger[] { 100 % -7, -47 % -7, 0, 15 % -7 }
        };
        // Boundary: Multi-word large numbers & dividend < divisor
        var large = BigInteger.Parse("123456789012345678901234567890123456789");
        var divisor = BigInteger.Parse("9876543210987654321");
        yield return new object[]
        {
            TestScenario.Boundary,
            new BigInteger[] { large, -large, 5, -5 },
            divisor,
            new BigInteger[] { large / divisor, -large / divisor, 0, 0 },
            new BigInteger[] { large % divisor, -large % divisor, 5, -5 }
        };
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(DivisionData))]
    public void SelectDivide_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal,
        BigInteger[] expectedDiv, BigInteger[] expectedMod)
    {
        using var array = CreateArray(input);
        using var scalar = CreateScalar(scalarVal);
        using var expr = array.AsVectorizedEnumerable().SelectDivide(scalar);
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for (int i = 0; i < input.Length; i++) Assert.Equal(expectedDiv[i], hostResult[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(DivisionData))]
    public void SelectModulo_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input, BigInteger scalarVal,
        BigInteger[] expectedDiv, BigInteger[] expectedMod)
    {
        using var array = CreateArray(input);
        using var scalar = CreateScalar(scalarVal);
        using var expr = array.AsVectorizedEnumerable().SelectModulo(scalar);
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for (int i = 0; i < input.Length; i++) Assert.Equal(expectedMod[i], hostResult[i]);
    }

    public static IEnumerable<object[]> ZipDivisionData()
    {
        yield return new object[]
        {
            TestScenario.HappyPath,
            new BigInteger[] { 100, -47, 25, -81 },
            new BigInteger[] { 7, 6, -4, -9 },
            new BigInteger[] { 100 / 7, -47 / 6, 25 / -4, -81 / -9 },
            new BigInteger[] { 100 % 7, -47 % 6, 25 % -4, -81 % -9 }
        };
        var large1 = BigInteger.Parse("99999999999999999999999999999999999999");
        var large2 = BigInteger.Parse("1234567890123456789012345");
        yield return new object[]
        {
            TestScenario.Boundary,
            new BigInteger[] { large1, -large1, 0, 3 },
            new BigInteger[] { large2, large2, 17, 10 },
            new BigInteger[] { large1 / large2, -large1 / large2, 0, 0 },
            new BigInteger[] { large1 % large2, -large1 % large2, 0, 3 }
        };
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ZipDivisionData))]
    public void ZipDivide_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input1, BigInteger[] input2,
        BigInteger[] expectedDiv, BigInteger[] expectedMod)
    {
        using var array1 = CreateArray(input1);
        using var array2 = CreateArray(input2);
        using var expr = array1.AsVectorizedEnumerable().ZipDivide(array2.AsVectorizedEnumerable());
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for (int i = 0; i < input1.Length; i++) Assert.Equal(expectedDiv[i], hostResult[i]);
    }

    [Theory]
    [Category(TestCategory.Unit)]
    [MemberData(nameof(ZipDivisionData))]
    public void ZipModulo_EvaluatesCorrectly(TestScenario scenario, BigInteger[] input1, BigInteger[] input2,
        BigInteger[] expectedDiv, BigInteger[] expectedMod)
    {
        using var array1 = CreateArray(input1);
        using var array2 = CreateArray(input2);
        using var expr = array1.AsVectorizedEnumerable().ZipModulo(array2.AsVectorizedEnumerable());
        using var result = expr.Evaluate(_context);
        var hostResult = ToHostArray(result);
        for (int i = 0; i < input1.Length; i++) Assert.Equal(expectedMod[i], hostResult[i]);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void ZipDivideAndModulo_MismatchedLengths_ThrowsArgumentException()
    {
        using var array1 = CreateArray(new BigInteger[] { 10, 20, 30 });
        using var array2 = CreateArray(new BigInteger[] { 2, 5 });

        using var divExpr = array1.AsVectorizedEnumerable().ZipDivide(array2.AsVectorizedEnumerable());
        Assert.Throws<ArgumentException>(() => divExpr.Evaluate(_context));

        using var modExpr = array1.AsVectorizedEnumerable().ZipModulo(array2.AsVectorizedEnumerable());
        Assert.Throws<ArgumentException>(() => modExpr.Evaluate(_context));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.HappyPath)]
    public void SelectSqrt_And_SelectDivideByUInt32_EvaluateCorrectly()
    {
        var largeRoot = BigInteger.Parse("12345678901234567890");
        var largeSquare = largeRoot * largeRoot + 12345;
        BigInteger[] sqrtInputs = [0, 1, 15, 16, 10000, largeSquare];
        BigInteger[] expectedSqrts = [0, 1, 3, 4, 100, largeRoot];

        using var sqrtArray = CreateArray(sqrtInputs);
        using var sqrtExpr = sqrtArray.AsVectorizedEnumerable().SelectSqrt();
        using var sqrtResult = sqrtExpr.Evaluate(_context);
        var hostSqrts = ToHostArray(sqrtResult);

        for (var i = 0; i < sqrtInputs.Length; i++)
        {
            Assert.Equal(expectedSqrts[i], hostSqrts[i]);
        }

        BigInteger[] divInputs = [100, -47, 0, -5, largeSquare, -largeSquare];
        const uint divisor = 7u;
        using var divArray = CreateArray(divInputs);
        using var divExpr = divArray.AsVectorizedEnumerable().SelectDivideByUInt32(divisor);
        using var divResult = divExpr.Evaluate(_context);
        var hostDivs = ToHostArray(divResult);

        for (var i = 0; i < divInputs.Length; i++)
        {
            Assert.Equal(divInputs[i] / divisor, hostDivs[i]);
        }

        // Verify -5 / 7 == 0 has sign normalized to 0 (non-negative)
        Assert.False(divResult.GetElement(3).IsNegative);
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void SelectSqrt_Negative_And_SelectDivideByUInt32_Zero_ThrowExpectedExceptions()
    {
        using var negArray = CreateArray([16, -9, 25]);
        using var sqrtExpr = negArray.AsVectorizedEnumerable().SelectSqrt();
        Assert.Throws<InvalidOperationException>(() => sqrtExpr.Evaluate(_context));

        using var divZeroExpr = negArray.AsVectorizedEnumerable().SelectDivideByUInt32(0u);
        Assert.Throws<DivideByZeroException>(() => divZeroExpr.Evaluate(_context));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.SadPath)]
    public void SelectDivideAndModulo_ZeroScalar_ThrowsDivideByZeroException()
    {
        using var array = CreateArray([100, -47, 25]);
        using var zeroScalar = CreateScalar(0);

        using var divExpr = array.AsVectorizedEnumerable().SelectDivide(zeroScalar);
        Assert.Throws<DivideByZeroException>(() => divExpr.Evaluate(_context));

        using var modExpr = array.AsVectorizedEnumerable().SelectModulo(zeroScalar);
        Assert.Throws<DivideByZeroException>(() => modExpr.Evaluate(_context));
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void SelectMultiplyAndZipMultiply_NegativeTimesZero_NormalizesSignToZero()
    {
        using var array = CreateArray([-42, 0, -999999999999]);
        using var zeroScalar = CreateScalar(0);

        using var selectMulExpr = array.AsVectorizedEnumerable().SelectMultiply(zeroScalar);
        using var selectMulResult = selectMulExpr.Evaluate(_context);
        for (var i = 0; i < array.Length; i++)
        {
            var elem = selectMulResult.GetElement(i);
            Assert.False(elem.IsNegative);
            Assert.Equal(BigInteger.Zero, ToBigInt(elem));
        }

        using var rhsArray = CreateArray([0, -77, 0]);
        using var zipMulExpr = array.AsVectorizedEnumerable().ZipMultiply(rhsArray.AsVectorizedEnumerable());
        using var zipMulResult = zipMulExpr.Evaluate(_context);
        for (var i = 0; i < array.Length; i++)
        {
            var elem = zipMulResult.GetElement(i);
            Assert.False(elem.IsNegative);
            Assert.Equal(BigInteger.Zero, ToBigInt(elem));
        }
    }

    [Fact]
    [Category(TestCategory.Unit)]
    [Scenario(TestScenario.Boundary)]
    public void SelectAndZipDivideModulo_LargeMultiLimbExceeding128Words_EvaluatesCorrectly()
    {
        const int numWords = 160;
        var p = (BigInteger.One << (140 * 32)) - 123456789;
        var q = (BigInteger.One << (75 * 32)) + 987654321;

        BigInteger[] dividends = [p, -p, q - 1];
        using var array = CreateArray(dividends, numWords);
        using var scalarDivisor = CreateScalar(q, numWords);

        using var divExpr = array.AsVectorizedEnumerable().SelectDivide(scalarDivisor);
        using var divResult = divExpr.Evaluate(_context);
        var hostDiv = ToHostArray(divResult);

        using var modExpr = array.AsVectorizedEnumerable().SelectModulo(scalarDivisor);
        using var modResult = modExpr.Evaluate(_context);
        var hostMod = ToHostArray(modResult);

        for (var i = 0; i < dividends.Length; i++)
        {
            Assert.Equal(dividends[i] / q, hostDiv[i]);
            Assert.Equal(dividends[i] % q, hostMod[i]);
        }
    }
}
