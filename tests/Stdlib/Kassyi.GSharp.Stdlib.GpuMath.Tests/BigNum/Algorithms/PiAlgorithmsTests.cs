using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms.Diagnostics;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Algorithms;

[TestTarget(ExecutionTarget.CpuExecution)]
public class PiAlgorithmsTests
{
    [Fact]
    public void Test_BbpAlgorithm_ExtractsHexDigitsCorrectly()
    {
        // Pi in hex: 3.243F6A8885A308D313198A2E03707344A4093822299F31D0082EFA98EC4E6C89...
        // Offset 0 starts from the first fractional digit '2'
        
        var first10 = BbpAlgorithm.ExtractHexDigits(0, 10);
        Assert.Equal("243F6A8885", first10.ToUpperInvariant());
        
        var next10 = BbpAlgorithm.ExtractHexDigits(10, 10);
        Assert.Equal("A308D31319", next10.ToUpperInvariant());

        var largeOff = BbpAlgorithm.ExtractHexDigits(10000, 2);
        Assert.Equal(2, largeOff.Length);
    }

    [Theory]
    [InlineData(10, "31415926535")] // 10 digits after 3, making length 11 (3.1415926535)
    [InlineData(50, "314159265358979323846264338327950288419716939937510")]
    public void Test_ChudnovskyAlgorithm_Cpu_ComputesPiCorrectly(int digits, string expectedDecimal)
    {
        var piString = ChudnovskyAlgorithm.ComputePi(digits);
        Assert.Equal(expectedDecimal, piString);
    }

    [SkippableTheory]
    [InlineData(10, false, "3.1415926535")]
    [InlineData(10, true, "3.243F6A88")]
    public void Test_ChudnovskyAlgorithm_Gpu_ComputesPiCorrectly(int digits, bool asHex, string expected)
    {
        CudaCapability.EnsureGpuAvailable();
        using var sw = new StringWriter();
        ChudnovskyAlgorithm.ComputePi(digits, sw, 1, asHex);
        var output = sw.ToString();
        Assert.Contains(expected.ToUpperInvariant(), output.ToUpperInvariant());
    }

    // Hex output keeps floor(digits * log2(10) / 32) words of fraction above the dropped margin, so the top word of
    // the result sits at index 40 + that count. 86 digits -> 8 words (index 48, below the BBP self-check);
    // 87..96 -> 9 words (index 49, the self-check's 64-bit tail would start inside the integer part);
    // 97 -> 10 words (index 50, first case with a valid self-check offset).
    [SkippableTheory]
    [Category(TestCategory.Stdlib)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(86)]
    [InlineData(87)]
    [InlineData(90)]
    [InlineData(96)]
    [InlineData(97)]
    public void Test_ChudnovskyAlgorithm_Gpu_Hex_MatchesMachinReference(int digits)
    {
        CudaCapability.EnsureGpuAvailable();
        using var sw = new StringWriter();

        // mutant: src/Stdlib/Kassyi.GSharp.Stdlib.GpuMath/BigNum/Algorithms/ChudnovskyAlgorithm.cs:302 `CheckIndex + 2 <= actualTopIndex` -> `CheckIndex + 1 <= actualTopIndex`
        // input: 87, 90, 96 (ArgumentOutOfRangeException from BbpAlgorithm.ExtractHexDigits)
        ChudnovskyAlgorithm.ComputePi(digits, sw, thresholdTerms: 8, asHex: true);

        var output = sw.ToString().Trim().ToUpperInvariant();
        var fractionHexDigits = (int)Math.Floor(digits * Math.Log2(10) / 32) * 8;

        // Every printed hex digit is a digit of pi: the reference is Machin's formula in BigInteger, independent of
        // both the Chudnovsky series and the BBP digit extraction that the algorithm uses for its own self-check.
        Assert.Equal("3." + MachinPiHexFraction(fractionHexDigits), output);
    }

    /// <summary>
    /// Returns the first <paramref name="hexDigits"/> hexadecimal digits of the fraction of pi, computed with
    /// Machin's formula <c>pi = 16 atan(1/5) - 4 atan(1/239)</c> in fixed point with 64 guard bits.
    /// </summary>
    private static string MachinPiHexFraction(int hexDigits)
    {
        const int GuardBits = 64;
        var scaleBits = hexDigits * 4 + GuardBits;
        var one = BigInteger.One << scaleBits;
        var pi = 16 * ArctanInverse(5, one) - 4 * ArctanInverse(239, one);

        // Each series term is truncated, so pi is off by at most a few units of 2^-scaleBits; 64 guard bits absorb
        // that unless pi has 64 equal bits in a row right after the requested digits (it does not at these sizes).
        var fraction = (pi >> GuardBits) - (new BigInteger(3) << (hexDigits * 4));
        return fraction.ToString("X").TrimStart('0').PadLeft(hexDigits, '0');
    }

    // atan(1/x) * one = sum_k (-1)^k * one / ((2k + 1) * x^(2k + 1)), truncated term by term.
    private static BigInteger ArctanInverse(int x, BigInteger one)
    {
        var sum = BigInteger.Zero;
        var power = one / x;
        var xSquared = x * x;
        for (var k = 0; !power.IsZero; k++)
        {
            var term = power / (2 * k + 1);
            sum += k % 2 == 0 ? term : -term;
            power /= xSquared;
        }

        return sum;
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PiVerifier_VerifyWithBbp_ValidatesCorrectAndIncorrectHexTails()
    {
        Assert.True(PiVerifier.VerifyWithBbp("243F6A88", 0));
        Assert.False(PiVerifier.VerifyWithBbp("DEADBEEF", 0));
        Assert.False(PiVerifier.VerifyWithBbp(string.Empty, 0));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void ChudnovskyAlgorithm_Cpu_InvalidArguments_ThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputePi(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputePi(-5));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputeSubtree(-1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputeSubtree(3, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChudnovskyAlgorithm.ComputeSubtree(4, 2));
    }
}
