#nullable enable
using System.Globalization;
using System.Text.Json;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

/// <summary>
/// The model of the Karp-Markstein wide integer square root in <see cref="BigInteger"/>, and the golden values of the
/// Python reference model it was derived from.
/// </summary>
internal static class SqrtKarpMarksteinReference
{
    private static readonly Lazy<IReadOnlyList<GoldenCase>> s_golden = new(LoadGolden);

    /// <summary>The golden cases from the Python reference model.</summary>
    internal static IReadOnlyList<GoldenCase> Golden => s_golden.Value;

    /// <summary>
    /// The word counts and bit shifts of the Karp-Markstein square root for a root of <see cref="RootWords"/> words
    /// <c>n</c>.
    /// </summary>
    /// <param name="RootWords">The root words <c>n</c>.</param>
    internal readonly record struct Widths(int RootWords)
    {
        /// <summary>The top operand words <c>w = n + 1</c> (<c>A = floor(a / 2^(32(n - 1)))</c>).</summary>
        public int W => RootWords + 1;

        /// <summary>The working words <c>p_y = max(3, floor(n / 2) + 1)</c> of the half-precision inverse.</summary>
        public int InverseWords => Math.Max(3, RootWords / 2 + 1);

        /// <summary>The dropped low words <c>j = floor(n / 2)</c> of <c>A_t = floor(A / 2^(32j))</c>.</summary>
        public int DroppedWords => RootWords / 2;

        /// <summary>The words <c>floor(n / 2) + 1</c> of <c>s0</c>.</summary>
        public int HalfRootWords => RootWords / 2 + 1;

        /// <summary>The words <c>ceil(n / 2) + 1</c> of <c>d_hi = A - s0^2</c>.</summary>
        public int ResidualWords => (RootWords + 1) / 2 + 1;

        /// <summary>The words <c>ceil(n / 2)</c> of <c>d'</c> and <c>delta</c>.</summary>
        public int StepWords => (RootWords + 1) / 2;

        /// <summary>The exponent <c>P = 16n + 14 + 32 p_y</c> (<c>y ~ 2^P / sqrt(A)</c>).</summary>
        public int P => 16 * RootWords + 14 + 32 * InverseWords;

        /// <summary>The left shift <c>16n - 16</c> that turns <c>s0</c> into <c>s = s0·2^(16n - 16)</c>.</summary>
        public int RootShift => 16 * RootWords - 16;
    }

    /// <summary>Every intermediate value and the final root of the Karp-Markstein step.</summary>
    internal sealed record Trace(
        IReadOnlyList<(int Words, BigInteger Y, BigInteger D)> Stages,
        BigInteger Y,
        BigInteger S0,
        BigInteger DHigh,
        BigInteger DShifted,
        BigInteger Delta,
        BigInteger Estimate,
        BigInteger Root);

    /// <summary>One golden case of the Python reference model.</summary>
    internal sealed record GoldenCase(int RootWords, string Label, int InverseWords, BigInteger A, Trace Trace);

    /// <summary>Runs the Karp-Markstein square root model on a normalized <c>2n</c>-word operand <c>a</c>.</summary>
    internal static Trace Run(BigInteger a, int rootWords)
    {
        var g = new Widths(rootWords);
        var top = a >> (32 * (rootWords - 1));
        var firstY = InverseSqrtDoublingReference.InitialGuess(top, 16 * g.W + 62);
        var stages = InverseSqrtDoublingReference.RunTo(top, g.W, firstY, g.InverseWords, out var stageY);
        var y = stageY - 2;
        var s0 = ((top >> (32 * g.DroppedWords)) * y) >> (g.P - 32 * g.DroppedWords);
        var dHigh = top - s0 * s0;
        var dShifted = dHigh >> 31;
        var delta = (y * dShifted) >> (32 * g.InverseWords);
        var estimate = (s0 << g.RootShift) + delta;
        return new Trace(stages, y, s0, dHigh, dShifted, delta, estimate, GpuBigIntSqrtTests.FloorSqrt(a));
    }

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
        var path = Path.Combine(AppContext.BaseDirectory, "BigNum", "TestData", "SqrtKarpMarksteinGolden.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var cases = new List<GoldenCase>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var stages = new List<(int Words, BigInteger Y, BigInteger D)>();
            foreach (var stage in element.GetProperty("stages").EnumerateArray())
            {
                stages.Add((
                    stage.GetProperty("p").GetInt32(),
                    InverseSqrtDoublingReference.ParseHex(GetString(stage, "Y")),
                    BigInteger.Parse(GetString(stage, "D"), CultureInfo.InvariantCulture)));
            }

            var trace = new Trace(
                stages,
                InverseSqrtDoublingReference.ParseHex(GetString(element, "y")),
                InverseSqrtDoublingReference.ParseHex(GetString(element, "s0")),
                InverseSqrtDoublingReference.ParseHex(GetString(element, "d_high")),
                InverseSqrtDoublingReference.ParseHex(GetString(element, "d_shifted")),
                InverseSqrtDoublingReference.ParseHex(GetString(element, "delta")),
                InverseSqrtDoublingReference.ParseHex(GetString(element, "estimate")),
                InverseSqrtDoublingReference.ParseHex(GetString(element, "root")));

            cases.Add(new GoldenCase(
                element.GetProperty("n").GetInt32(),
                GetString(element, "label"),
                element.GetProperty("py").GetInt32(),
                InverseSqrtDoublingReference.ParseHex(GetString(element, "a")),
                trace));
        }

        return cases;
    }

    private static string GetString(JsonElement element, string name) =>
        element.GetProperty(name).GetString()
        ?? throw new InvalidDataException($"The golden value {name} is not a string.");
}
