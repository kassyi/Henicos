#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// A big-float Pow input <c>x = XSignificand · 2^XExponent</c>, <c>y = ±YSignificand · 2^YExponent</c>, with the number
/// of result words <c>n</c> (<c>p = 32·n</c>).
/// </summary>
internal readonly record struct PowInput(
    BigInteger XSignificand, int XExponent, BigInteger YSignificand, int YExponent, bool YNegative, int Words);

/// <summary>
/// Pow inputs whose power is not a <c>p</c>-bit value but lies closer to one than the first approximation resolves, so
/// that the rounding check (Ziv) must reject it and retry with more guard words.
/// </summary>
/// <remarks>
/// <para>
/// "NearExact": <c>x</c> or <c>y</c> moved by about <c>2^-200</c> from an exact power (<c>2^3</c>, <c>4^0.5</c>,
/// <c>(9/4)^0.5</c>), from above and from below; <c>p = 128</c>, the first approximation has <c>W = 160</c> bits.
/// </para>
/// <para>
/// "LongerThanP": <c>(2^100 + 1)^1</c> at <c>p = 32</c>, a dyadic power longer than <c>p</c> bits whose bits after the top
/// 32 are 67 zeros and a one: the check retries until <c>W</c> passes about 100 bits.
/// </para>
/// </remarks>
internal static class PowRetryInputs
{
    /// <summary>The named inputs.</summary>
    public static readonly Dictionary<string, PowInput> Cases = Create();

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var name in Cases.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    private static Dictionary<string, PowInput> Create()
    {
        var one = BigInteger.One;
        return new Dictionary<string, PowInput>
        {
            ["PowNearExact/(2+2^-200)^3"] = new((one << 201) + 1, -200, 3, 0, false, 4),
            ["PowNearExact/(2-2^-200)^3"] = new((one << 201) - 1, -200, 3, 0, false, 4),
            ["PowNearExact/2^(3+2^-200)"] = new(2, 0, (3 * (one << 200)) + 1, -200, false, 4),
            ["PowNearExact/2^(3-2^-200)"] = new(2, 0, (3 * (one << 200)) - 1, -200, false, 4),
            ["PowNearExact/4^(0.5+2^-201)"] = new(4, 0, (one << 200) + 1, -201, false, 4),
            ["PowNearExact/4^(0.5-2^-201)"] = new(4, 0, (one << 200) - 1, -201, false, 4),
            ["PowNearExact/((9/4)-2^-200)^0.5"] = new((9 * (one << 198)) - 1, -200, 1, -1, false, 4),
            ["PowLongerThanP/(2^100+1)^1/n=1"] = new((one << 100) + 1, 0, 1, 0, false, 1),
        };
    }
}
