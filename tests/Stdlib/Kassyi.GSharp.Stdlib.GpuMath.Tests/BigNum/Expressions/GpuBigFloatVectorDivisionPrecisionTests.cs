#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// Float vector division (<c>SelectDivide</c>, <c>ZipDivide</c>, <c>SelectDivideByUInt32</c>) returns each quotient
/// rounded toward zero to the precision of the result element (32 bits per word) and normalized to its top bit,
/// whatever the normalization of the dividend and the divisor.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class GpuBigFloatVectorDivisionPrecisionTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBigFloatVectorDivisionPrecisionTests()
    {
        CudaCapability.EnsureGpuAvailable();
        _scope = new GpuKernelLoader.CudaContextScope();
        _workspace = new GpuWorkspace(1024 * 1024, GpuExecutionEngine.GSharpAot);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        _scope.Dispose();
    }

    private static readonly BigInteger AllOnes64 = (BigInteger.One << 64) - 1;

    // label, dividend significand, dividend words, dividend exponent, dividend negative, divisor significand, divisor
    // words, divisor exponent, divisor negative.
    public static TheoryData<string, BigInteger, int, int, bool, BigInteger, int, int, bool> FloatDivisorRows => new()
    {
        // The dividend 1.0 is not normalized: a fixed 32-bit pre-shift kept only 32 quotient bits (#1462).
        { "1 / 3, unnormalized dividend", 1, 2, 0, false, 3, 1, 0, false },
        // The aligned significand of the dividend is below the divisor's, so the quotient needs one more bit.
        { "1 / 3 with exponents", 1, 2, 5, false, 3, 1, -7, false },
        // The aligned significand of the dividend is above the divisor's: no extra bit.
        { "3 / 1", 3, 2, 0, false, 1, 1, 0, false },
        { "normalized dividend / 3", BigInteger.One << 63, 2, 0, false, 3, 1, 0, false },
        { "all ones / all ones, 2 words", AllOnes64, 2, 0, false, AllOnes64, 2, 0, false },
        { "2^63 / (2^63 + 1)", BigInteger.One << 63, 2, 0, false, (BigInteger.One << 63) + 1, 2, 0, false },
        { "1 / 0xFFFFFFFF", 1, 2, 0, false, 0xFFFFFFFFu, 1, 0, false },
        { "exact power of two", 12345, 2, 3, false, 4, 1, 0, false },
        { "negative / positive", 7, 2, 0, true, 3, 1, 0, false },
        { "negative / negative", 7, 2, 0, true, 3, 1, 0, true },
        { "zero dividend", 0, 2, 0, false, 3, 1, 0, false },
        // 33-word elements: the quotient words cross a warp of the division kernel.
        { "1 / 3, 33 words", 1, 33, 0, false, 3, 1, 0, false },
        { "unnormalized wide divisor", 5, 2, 0, false, 3, 2, 0, false }
    };

    // Properties, with the expected quotient built from the inputs of the test:
    // 1. Each element is RZ_p(x / y) for p = 32 * (result words): a significand of exactly p bits with
    //    sig * 2^exp <= |x / y| < (sig + 1) * 2^exp (BigFloatReference.IsRoundedTowardZero), or 0 for x = 0.
    // 2. The sign is the exclusive or of the operand signs (0 has no sign).
    // mutant: GpuBigIntVectorKernels.DivMod.cu.cs DeviceVectorFloatDivParallel `precision - 1 - bitsA + bitsB` ->
    // `32` (the old fixed pre-shift: every nonzero row)
    // mutant: NormalizeQuotientWarp `if (topBit != 0u)` -> `if (true)`, the extra bit never taken (the rows whose
    // aligned dividend is below the divisor, "1 / 3 ...", "2^63 / (2^63 + 1)", "1 / 0xFFFFFFFF", "normalized dividend
    // / 3", "unnormalized wide divisor": p - 1 bits; "3 / 1" needs no extra bit and passes)
    // mutant: DeviceVectorFloatDivParallel `- extraBit` removed from the exponent (the same rows: twice the value)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(FloatDivisorRows))]
    public void GpuBigFloatArray_SelectDivideAndZipDivide_RoundTowardZeroToTheResultPrecision(
        string label,
        BigInteger dividend,
        int dividendWords,
        int dividendExponent,
        bool dividendNegative,
        BigInteger divisor,
        int divisorWords,
        int divisorExponent,
        bool divisorNegative)
    {
        using var dividends = new GpuBigFloatArray(_workspace, 1, dividendWords);
        using (var element = NewFloat(dividend, dividendWords, dividendExponent, dividendNegative))
        {
            dividends.SetElement(0, element);
        }

        using var divisors = new GpuBigFloatArray(_workspace, 1, divisorWords);
        using var scalar = NewFloat(divisor, divisorWords, divisorExponent, divisorNegative);
        divisors.SetElement(0, scalar);

        using var bySelect = dividends.AsVectorizedEnumerable().SelectDivide(scalar);
        using var byZip = dividends.AsVectorizedEnumerable().ZipDivide(divisors.AsVectorizedEnumerable());
        var resultWords = Math.Max(dividendWords, divisorWords) + 1;

        foreach (var (name, expression) in new[] { ("SelectDivide", bySelect), ("ZipDivide", byZip) })
        {
            var result = expression.Evaluate(_workspace);
            Assert.Equal(resultWords, result.WordsPerElement);
            AssertQuotient($"{label}, {name}", result.GetElement(0), 32 * resultWords, dividend, dividendExponent,
                dividendNegative, divisor, divisorExponent, divisorNegative);
        }
    }

    // label, dividend significand, words, exponent, negative, divisor.
    public static TheoryData<string, BigInteger, int, int, bool, uint> UInt32DivisorRows => new()
    {
        // A significand below the divisor gave exactly 0, and every quotient lost up to 32 bits (#1349).
        { "1 / 3", 1, 2, 0, false, 3u },
        { "3 / 3", 3, 2, 0, false, 3u },
        { "1 / 1", 1, 2, 0, false, 1u },
        { "normalized / 2^31", BigInteger.One << 63, 2, 0, false, 0x80000000u },
        { "all ones / 0xFFFFFFFF", AllOnes64, 2, 4, false, 0xFFFFFFFFu },
        { "1 / 3, 1 word", 1, 1, -3, false, 3u },
        { "1 / 3, 33 words", 1, 33, 0, false, 3u },
        { "negative", 10, 2, 0, true, 7u },
        { "zero", 0, 2, 0, false, 3u }
    };

    // Properties 1 and 2 above, with p = 32 * (element words): the result keeps the width of the source.
    // mutant: FloatVectorUnaryExpressions.cs FloatVectorDivByUInt32Expression the divisor operand exponent `0` -> `1`
    // (every nonzero row: half the value)
    // The old integer division (arithmetic.DivideByUInt32, exponent kept) failed every row but "zero" before the fix.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(UInt32DivisorRows))]
    public void GpuBigFloatArray_SelectDivideByUInt32_RoundsTowardZeroToTheElementPrecision(
        string label,
        BigInteger dividend,
        int words,
        int exponent,
        bool negative,
        uint divisor)
    {
        using var dividends = new GpuBigFloatArray(_workspace, 2, words);
        using (var element = NewFloat(dividend, words, exponent, negative))
        {
            dividends.SetElement(0, element);
            dividends.SetElement(1, element);
        }

        using var expression = dividends.AsVectorizedEnumerable().SelectDivideByUInt32(divisor);
        var result = expression.Evaluate(_workspace);

        Assert.Equal(words, result.WordsPerElement);
        for (var i = 0; i < 2; i++)
        {
            AssertQuotient($"{label}, element {i}", result.GetElement(i), 32 * words, dividend, exponent, negative,
                divisor, 0, false);
        }
    }

    // A zero divisor is an error before anything runs.
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void GpuBigFloatArray_SelectDivideByUInt32_ZeroDivisor_Throws()
    {
        using var dividends = new GpuBigFloatArray(_workspace, 1, 2);
        using var expression = dividends.AsVectorizedEnumerable().SelectDivideByUInt32(0u);

        Assert.Throws<DivideByZeroException>(() => expression.Evaluate(_workspace));
    }

    private static void AssertQuotient(
        string label,
        scoped GpuBigFloatView element,
        int precision,
        BigInteger dividend,
        int dividendExponent,
        bool dividendNegative,
        BigInteger divisor,
        int divisorExponent,
        bool divisorNegative)
    {
        var significand = element.Significand.ToBigInteger();
        var actual = BigFloatReference.Describe(significand, element.Exponent, element.IsNegative);

        // |x / y| = (dividend * 2^dividendExponent) / (divisor * 2^divisorExponent) as num / den.
        var shift = dividendExponent - divisorExponent;
        var num = shift >= 0 ? dividend << shift : dividend;
        var den = shift >= 0 ? divisor : divisor << -shift;
        Assert.True(
            BigFloatReference.IsRoundedTowardZero(significand, element.Exponent, num, den, precision),
            $"{label}: {actual} is not RZ_{precision}({num} / {den})");

        if (!significand.IsZero)
        {
            Assert.True(element.IsNegative == (dividendNegative != divisorNegative), $"{label}: sign of {actual}");
        }
    }

    private GpuBigFloat NewFloat(BigInteger magnitude, int words, int exponent, bool negative)
    {
        var data = new uint[words];
        for (var w = 0; w < words; w++)
        {
            data[w] = (uint)(magnitude >> (32 * w) & uint.MaxValue);
        }

        return new GpuBigFloat(new GpuBigUInt(_workspace, data), exponent, negative);
    }
}
