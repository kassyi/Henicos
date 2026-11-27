#nullable enable
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

    /// <summary>
    /// Asserts that every stage, intermediate value and final root matches <paramref name="expected"/>.
    /// </summary>
    internal static void AssertTraceEquals(Trace expected, Trace actual, string label)
    {
        Assert.Equal(expected.Stages.Count, actual.Stages.Count);
        for (var k = 0; k < expected.Stages.Count; k++)
        {
            Assert.True(
                expected.Stages[k].Words == actual.Stages[k].Words
                && expected.Stages[k].Y == actual.Stages[k].Y
                && expected.Stages[k].D == actual.Stages[k].D,
                $"{label}: stage {k} (p = {actual.Stages[k].Words}) differs from the model");
        }

        Assert.Equal(expected.Y, actual.Y);
        Assert.Equal(expected.S0, actual.S0);
        Assert.Equal(expected.DHigh, actual.DHigh);
        Assert.Equal(expected.DShifted, actual.DShifted);
        Assert.Equal(expected.Delta, actual.Delta);
        Assert.Equal(expected.Estimate, actual.Estimate);
        Assert.Equal(expected.Root, actual.Root);
    }

    /// <summary>
    /// Asserts K-P2 (<c>0 &lt;= floor(2^P / sqrt(A)) - y &lt;= 3</c>), K-P3 (buffer bounds for <c>s0</c>, <c>d_hi</c>,
    /// <c>d'</c>, <c>delta</c>) and K-P4 (<c>floor(sqrt(a)) - r'' in {0, 1}</c>). Returns <c>floor(2^P / sqrt(A)) - y</c>.
    /// </summary>
    internal static BigInteger AssertTraceProperties(BigInteger a, int rootWords, Trace trace, string label)
    {
        var g = new Widths(rootWords);
        var top = a >> (32 * (rootWords - 1));

        // K-P2: 0 <= floor(2^P / sqrt(A)) - y <= 3 (never above the root).
        var yFloor = InverseSqrtDoublingReference.FloorScaledInverseSqrt(top, g.P);
        var yGap = yFloor - trace.Y;
        Assert.True(yGap >= 0 && yGap <= 3, $"{label}: floor(2^P / sqrt(A)) - y = {yGap}");

        // K-P3: the bounds that size the buffers.
        var sqrtTop = GpuBigIntSqrtTests.FloorSqrt(top);
        Assert.True(trace.S0 <= sqrtTop, $"{label}: s0 exceeds floor(sqrt(A))");
        Assert.Equal(top - trace.S0 * trace.S0, trace.DHigh);
        Assert.True(trace.DHigh.Sign >= 0 && trace.DHigh < (BigInteger.One << (16 * rootWords + 21)),
            $"{label}: d_hi out of [0, 2^(16n + 21))");
        var stepBound = BigInteger.One << (32 * g.StepWords);
        Assert.True(trace.DShifted.Sign >= 0 && trace.DShifted < stepBound, $"{label}: d' out of stepWords");
        Assert.True(trace.Delta.Sign >= 0 && trace.Delta < stepBound, $"{label}: delta out of stepWords");

        // K-P4: floor(sqrt(a)) - r'' in {0, 1}.
        var gap = trace.Root - trace.Estimate;
        Assert.True(gap >= 0 && gap <= 1, $"{label}: floor(sqrt(a)) - r'' = {gap}");
        return yGap;
    }

    private static IReadOnlyList<GoldenCase> LoadGolden()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "BigNum", "TestData", "SqrtKarpMarksteinGolden.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var cases = new List<GoldenCase>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var trace = new Trace(
                InverseSqrtDoublingReference.ReadStagesWithResidual(element),
                InverseSqrtDoublingReference.ReadHex(element, "y"),
                InverseSqrtDoublingReference.ReadHex(element, "s0"),
                InverseSqrtDoublingReference.ReadHex(element, "d_high"),
                InverseSqrtDoublingReference.ReadHex(element, "d_shifted"),
                InverseSqrtDoublingReference.ReadHex(element, "delta"),
                InverseSqrtDoublingReference.ReadHex(element, "estimate"),
                InverseSqrtDoublingReference.ReadHex(element, "root"));

            cases.Add(new GoldenCase(
                element.GetProperty("n").GetInt32(),
                InverseSqrtDoublingReference.GetString(element, "label"),
                element.GetProperty("py").GetInt32(),
                InverseSqrtDoublingReference.ReadHex(element, "a"),
                trace));
        }

        return cases;
    }
}
