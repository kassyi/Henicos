#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// Inputs of the big-float Log / Exp tests whose exact result lies next to a <c>p</c>-bit boundary <c>T</c>
/// (<c>p = 128</c>, first working precision <c>W = 160</c>), so that the rounding check (Ziv) must reject the first
/// approximation and retry with more guard words.
/// </summary>
/// <remarks>
/// <para>
/// "Above" has the exact value just above <c>T</c> and "Below" just below it, by about <c>2^-200</c> relative. Which
/// half of the check rejects these depends on the sign of the error of the first approximation.
/// </para>
/// <para>
/// "DistanceAbove" puts the exact value <c>d</c> units of the last bit at <c>W</c> bits above <c>T</c>, with
/// <c>0 &lt; d &lt; B</c> and the actual error far below <c>d</c>: the guard of the first approximation is then below
/// <c>B</c> whatever the sign of its error, so the lower half of the check (<c>B ≤ G</c>) rejects it; a check
/// without that half would accept it at once.
/// </para>
/// <para>
/// The inputs are built with the interval oracle (Log) or a high-precision CPU approximation of <c>ln</c> (Exp); the
/// expected values of the tests still come from the oracle.
/// </para>
/// </remarks>
internal static class ZivRetryInputs
{
    /// <summary>The named inputs: the function, the argument <c>±Significand · 2^Exponent</c>.</summary>
    public static readonly Dictionary<string, (string Kind, BigInteger Significand, int Exponent, bool Negative)> Cases =
        Create();

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in Cases.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    private static Dictionary<string, (string, BigInteger, int, bool)> Create()
    {
        var cases = new Dictionary<string, (string, BigInteger, int, bool)>();
        var one = BigInteger.One;

        // ln x = T ± tiny with T = 2^-127 · (2^127 + 12345) in [1, 2): x = RZ_200(e^T) (ln x just below T) and the next
        // 200-bit value (just above). The approximation of ln x lies below T, so its guard is near 2^32.
        var t = (one << 127) + 12345;
        var (below, belowExponent) = BigFloatIntervalOracle.ExpRz(t, 127, 200);
        cases["ZivRetry/LogBelow"] = ("Log", below, (int)belowExponent, false);
        cases["ZivRetry/LogAbove"] = ("Log", below + 1, (int)belowExponent, false);

        // ln x = -(T ± tiny) with T = 2^-129 · (2^127 + 2^125 + 777) ≈ 0.31: x = RZ_200(e^-T) (|ln x| just above T) and
        // the next value (|ln x| just below T). x in [1/2, 3/4) subtracts 2yT from ln 2, so |A| lies above T and its
        // guard is near 0.
        var tn = (one << 127) + (one << 125) + 777;
        var (negative, negativeExponent) = BigFloatIntervalOracle.ExpRz(-tn, 129, 200);
        cases["ZivRetry/LogNegativeBelow"] = ("Log", negative, (int)negativeExponent, false);
        cases["ZivRetry/LogNegativeAbove"] = ("Log", negative + 1, (int)negativeExponent, false);

        // e^x = T ± tiny with T = 2^-127 · (2^127 + 2^126 + 4242) in [1.5, 2): x = ln T truncated to 200 fraction bits
        // and the next value.
        var te = (one << 127) + (one << 126) + 4242;
        var x = LnTruncated(te, -127, 200);
        cases["ZivRetry/ExpBelow"] = ("Exp", x, -200, false);
        cases["ZivRetry/ExpAbove"] = ("Exp", x + 1, -200, false);

        // ln x = T + 3 units - tiny, T = 2^-147 · (2^127 + 98765) in [2^-20, 2^-19), unit 2^(-20 - 159) = 2^-179:
        // x = RZ_220(e^(T + 3 · 2^-179)) is close to 1 (Horner form, B = 7), so G is about 3 < B.
        var target = ((one << 127) + 98765 << 32) + 3;
        var (close, closeExponent) = BigFloatIntervalOracle.ExpRz(target, 179, 220);
        cases["ZivRetry/LogDistanceAbove"] = ("Log", close, (int)closeExponent, false);

        // e^x = T + 11 units - tiny, T = 2^-127 · (2^127 + 2^126 + 31415) in [1.5, 2), unit 2^-159: x = ln(T + 11 · 2^-159)
        // truncated to 220 fraction bits (s = 2, B = 23), so G is about 11 < B.
        var expTarget = ((one << 127) + (one << 126) + 31415 << 32) + 11;
        cases["ZivRetry/ExpDistanceAbove"] = ("Exp", LnTruncated(expTarget, -159, 220), -220, false);
        return cases;
    }

    // floor(ln(significand · 2^exponent) · 2^fractionBits) from a 512-bit CPU approximation of the logarithm (its error
    // of a few units at 2^-540 cannot move a floor at 2^-220 unless the value is within that distance of a multiple).
    private static BigInteger LnTruncated(BigInteger significand, int exponent, int fractionBits)
    {
        var ln = BigFloatTranscendentalMath.LogApproximation(
            significand, exponent, 512, new GpuBigFloatTranscendentalOptions());
        return ln.Significand >> (int)(-fractionBits - ln.Exponent);
    }
}
