#nullable enable
using System.Globalization;
using System.Text.Json;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// The model of the precision-doubling inverse square root in <see cref="BigInteger"/>, stage by stage, and the golden
/// values of the reference model it was derived from.
/// </summary>
internal static class InverseSqrtDoublingReference
{
    private static readonly Lazy<IReadOnlyList<GoldenCase>> s_golden = new(LoadGolden);

    /// <summary>The golden cases: inputs, initial guesses and every stage of the reference model.</summary>
    internal static IReadOnlyList<GoldenCase> Golden => s_golden.Value;

    /// <summary>
    /// The scales of the inverse square root behind <c>SqrtFloor</c> for a root of <see cref="RootWords"/> words
    /// <c>n</c>.
    /// </summary>
    /// <param name="RootWords">The root words <c>n</c>.</param>
    internal readonly record struct Geometry(int RootWords)
    {
        /// <summary>The operand words <c>w = n + 1</c>.</summary>
        public int W => RootWords + 1;

        /// <summary>The margin words <c>m = ceil(n / 2)</c>.</summary>
        public int M => (RootWords + 1) / 2;

        /// <summary>The scale <c>B = 32(w + m)</c> of <c>x = 2^B / sqrt(A)</c>.</summary>
        public int B => 32 * (W + M);

        /// <summary>The scale <c>E = 16w + 32m + 2</c>: <c>Y</c> approximates <c>x / 2^E</c>.</summary>
        public int E => 16 * W + 32 * M + 2;

        /// <summary>The working words <c>p_K = w + 1</c> of the last stage.</summary>
        public int FinalWords => W + 1;

        /// <summary>
        /// The exponent <c>B + 32p - E</c> with <c>floor(2^exponent / sqrt(A)) = floor(Y*)</c> at <c>p</c> words.
        /// </summary>
        public int YExponent(int words) => B + 32 * words - E;
    }

    /// <summary>One golden case of the reference model.</summary>
    internal sealed record GoldenCase(int RootWords, string Label, int MarginWords, BigInteger A, BigInteger X0,
        IReadOnlyList<(int Words, BigInteger Y)> Stages, BigInteger XTilde);

    /// <summary>The working words of the stages, first stage first: <c>p_(k-1) = ceil(p_k / 2) + 1</c>.</summary>
    internal static List<int> Schedule(int finalWords)
    {
        var stages = new List<int> { finalWords };
        while (stages[^1] > 2)
        {
            var last = stages[^1];
            var next = (last + 1) / 2 + 1;
            if (next >= last)
            {
                next = Math.Max(2, last - 1);
            }

            stages.Add(next);
        }

        stages.Reverse();
        return stages;
    }

    /// <summary>
    /// The initial guess <c>x0</c> of <c>ComputeInitialGuess(isSquareRoot: true)</c> for <c>targetBits = B</c>.
    /// </summary>
    internal static BigInteger InitialGuess(BigInteger a, int targetBits)
    {
        var actualBits = (int)a.GetBitLength();
        var topIndex = (actualBits - 1) / 32;
        var words = Math.Min(4, topIndex + 1);
        var top = a >> (32 * (topIndex - words + 1));
        var truncatedBits = (int)top.GetBitLength();
        var shift = truncatedBits - 62;
        if (shift % 2 != 0)
        {
            shift++;
        }

        var original = shift >= 0 ? top >> shift : top << -shift;
        var initial = (BigInteger.One << 62) / (GpuBigIntSqrtTests.FloorSqrt(original) + 1);
        var finalShift = targetBits - (actualBits - truncatedBits + shift) / 2 - 62;
        return finalShift >= 0 ? initial << finalShift : initial >> -finalShift;
    }

    /// <summary><c>floor(value / 2^bits)</c>, also for a negative value.</summary>
    internal static BigInteger FloorShift(BigInteger value, int bits) =>
        value.Sign >= 0 ? value >> bits : -((-value + (BigInteger.One << bits) - 1) >> bits);

    /// <summary>
    /// One stage at <paramref name="words"/> working words: the next <c>Y</c> and the residual <c>D</c>.
    /// </summary>
    internal static (BigInteger Y, BigInteger D) Stage(BigInteger y, int words, BigInteger a, int operandWords)
    {
        var aWords = words + 1;
        var aTop = operandWords >= aWords ? a >> (32 * (operandWords - aWords)) : a << (32 * (aWords - operandWords));
        var s = (y * y) >> (32 * (words - 1));
        var d = FloorShift((BigInteger.One << (64 * words + 60)) - aTop * s, 32 * words + 60);
        var dWords = Math.Max(2, (words + 1) / 2);
        var q = Math.Min(words, dWords);
        var step = FloorShift((y >> (32 * (words - q))) * d, 32 * q + 1);
        return (y + step, d);
    }

    /// <summary>
    /// Runs every stage of <see cref="Schedule"/> up to <paramref name="finalWords"/> from <paramref name="firstY"/>
    /// and returns them; <paramref name="lastY"/> receives <c>Y_K</c> of the last stage.
    /// </summary>
    internal static List<(int Words, BigInteger Y, BigInteger D)> RunTo(BigInteger a, int operandWords,
        BigInteger firstY, int finalWords, out BigInteger lastY)
    {
        var stages = new List<(int Words, BigInteger Y, BigInteger D)>();
        var previous = 2;
        var y = firstY;
        foreach (var words in Schedule(finalWords))
        {
            y <<= 32 * (words - previous);
            var (next, d) = Stage(y, words, a, operandWords);
            y = next;
            stages.Add((words, y, d));
            previous = words;
        }

        lastY = y;
        return stages;
    }

    /// <summary>
    /// Runs every stage from <paramref name="firstY"/> and returns them; <paramref name="xTilde"/> receives
    /// <c>x~ = floor(Y_K / 2^(32 p_K - E))</c>.
    /// </summary>
    internal static List<(int Words, BigInteger Y, BigInteger D)> Run(BigInteger a, Geometry g, BigInteger firstY,
        out BigInteger xTilde)
    {
        var stages = RunTo(a, g.W, firstY, g.FinalWords, out var lastY);
        xTilde = lastY >> (32 * g.FinalWords - g.E);
        return stages;
    }

    /// <summary>The input <c>floor(x0 / 2^(E - 64))</c> of the first stage.</summary>
    internal static BigInteger FirstY(BigInteger a, Geometry g) => InitialGuess(a, g.B) >> (g.E - 64);

    /// <summary><c>floor(2^exponent / sqrt(a)) = isqrt(floor(2^(2 exponent) / a))</c>.</summary>
    internal static BigInteger FloorScaledInverseSqrt(BigInteger a, int exponent) =>
        GpuBigIntSqrtTests.FloorSqrt((BigInteger.One << (2 * exponent)) / a);

    /// <summary>Parses a hexadecimal <c>0x...</c> string as a non-negative value.</summary>
    internal static BigInteger ParseHex(string text) =>
        BigInteger.Parse(string.Concat("0", text.AsSpan(2)), NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture);

    /// <summary>The golden cases as (index, label, root words).</summary>
    public static TheoryData<int, string, int> GoldenCases()
    {
        var data = new TheoryData<int, string, int>();
        for (var index = 0; index < Golden.Count; index++)
        {
            data.Add(index, Golden[index].Label, Golden[index].RootWords);
        }

        return data;
    }

    private static IReadOnlyList<GoldenCase> LoadGolden()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "BigNum", "TestData", "InverseSqrtDoublingGolden.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var cases = new List<GoldenCase>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var stages = new List<(int Words, BigInteger Y)>();
            foreach (var stage in element.GetProperty("stages").EnumerateArray())
            {
                stages.Add((stage.GetProperty("p").GetInt32(), ParseHex(GetString(stage, "Y"))));
            }

            cases.Add(new GoldenCase(
                element.GetProperty("n").GetInt32(),
                GetString(element, "label"),
                element.GetProperty("m").GetInt32(),
                ParseHex(GetString(element, "A")),
                ParseHex(GetString(element, "x0")),
                stages,
                ParseHex(GetString(element, "x_tilde"))));
        }

        return cases;
    }

    private static string GetString(JsonElement element, string name) =>
        element.GetProperty(name).GetString()
        ?? throw new InvalidDataException($"The golden value {name} is not a string.");
}
