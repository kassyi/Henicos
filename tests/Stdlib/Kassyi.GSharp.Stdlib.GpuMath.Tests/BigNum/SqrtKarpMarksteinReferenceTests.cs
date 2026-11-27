#nullable enable
using Geometry = Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.InverseSqrtDoublingReference.Geometry;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// Properties checked here (property-test-design §1):
// - R-P1: the C# BigInteger port agrees bit for bit with every golden case from the independent Python model.
// - R-P2: floor(InitialGuess(A, B) / 2^(E - 64)) == InitialGuess(A, 16w + 62), so the host-computed firstY replaces
//   the full-width x0 upload without changing any bit.
// - K-P2, K-P3, K-P4: across structured and random operands on the CPU model, y stays within budget below
//   floor(2^P / sqrt(A)), every intermediate value fits its buffer bound, and floor(sqrt(a)) - r'' in {0, 1}.
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class SqrtKarpMarksteinReferenceTests
{
    private static readonly string[] s_sqrtFloorKinds =
    [
        "TopSquareMaxLow", "Random", "RandomOddLength", "Square", "SquareMinus1", "SquarePlus2R", "Lowest", "Highest",
    ];

    // R-P1: the C# reference port matches every stage, every intermediate value and the root of the Python golden set.
    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [MemberData(nameof(SqrtKarpMarksteinReference.GoldenCases), MemberType = typeof(SqrtKarpMarksteinReference))]
    public void SqrtKarpMarksteinReference_GoldenCase_MatchesThePythonModel(int index, string label, int rootWords)
    {
        var golden = SqrtKarpMarksteinReference.Golden[index];
        var g = new SqrtKarpMarksteinReference.Widths(rootWords);
        Assert.Equal(golden.InverseWords, g.InverseWords);

        var actual = SqrtKarpMarksteinReference.Run(golden.A, rootWords);
        var expected = golden.Trace;

        Assert.Equal(expected.Stages.Count, actual.Stages.Count);
        for (var k = 0; k < expected.Stages.Count; k++)
        {
            Assert.True(
                expected.Stages[k].Words == actual.Stages[k].Words
                && expected.Stages[k].Y == actual.Stages[k].Y
                && expected.Stages[k].D == actual.Stages[k].D,
                $"{label} n={rootWords}: stage {k} differs from golden");
        }

        Assert.Equal(expected.Y, actual.Y);
        Assert.Equal(expected.S0, actual.S0);
        Assert.Equal(expected.DHigh, actual.DHigh);
        Assert.Equal(expected.DShifted, actual.DShifted);
        Assert.Equal(expected.Delta, actual.Delta);
        Assert.Equal(expected.Estimate, actual.Estimate);
        Assert.Equal(expected.Root, actual.Root);
    }

    /// <summary>Operand word counts <c>w</c> for testing the first-stage initial-guess identity.</summary>
    public static TheoryData<int> FirstYWordCounts()
    {
        var data = new TheoryData<int>();
        for (var w = 2; w <= 40; w++)
        {
            data.Add(w);
        }

        data.Add(47);
        data.Add(101);
        return data;
    }

    // R-P2: floor(InitialGuess(A, B) / 2^(E - 64)) == InitialGuess(A, 16w + 62) for normalized operands.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(FirstYWordCounts))]
    public void SqrtKarpMarksteinReference_FirstY_EqualsTheShiftedInitialGuess(int operandWords)
    {
        var g = new Geometry(operandWords - 1);
        var bits = 32 * operandWords;
        var seed = 6007 + 31 * operandWords;
        var random = new Random(seed);

        var operands = new List<BigInteger>(23)
        {
            BigInteger.One << (bits - 2),
            (BigInteger.One << bits) - 1,
            BigInteger.One << (bits - 1),
        };
        for (var i = 0; i < 20; i++)
        {
            var bitLength = (i & 1) == 0 ? bits : bits - 1;
            operands.Add(GpuBigIntSqrtTests.RandomWithBitLength(random, bitLength));
        }

        for (var i = 0; i < operands.Count; i++)
        {
            var a = operands[i];
            var shifted = InverseSqrtDoublingReference.FirstY(a, g);
            var direct = InverseSqrtDoublingReference.InitialGuess(a, 16 * operandWords + 62);
            Assert.True(shifted == direct,
                $"w={operandWords} seed={seed} case={i}: FirstY ({shifted:X}) != direct ({direct:X})");
        }
    }

    /// <summary>Root word counts for testing the Karp-Markstein bounds on the CPU model.</summary>
    public static TheoryData<int> ModelRootWordCounts()
    {
        var data = new TheoryData<int>();
        foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 15, 16, 17, 31, 32, 33, 45, 46, 47, 64, 65 })
        {
            data.Add(n);
        }

        return data;
    }

    // K-P2, K-P3, K-P4 on the CPU reference model across structured and random operands.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ModelRootWordCounts))]
    public void SqrtKarpMarksteinReference_ManyOperands_LandsOnTheFloorOrOneBelow(int rootWords)
    {
        var g = new SqrtKarpMarksteinReference.Widths(rootWords);
        var seed = 5113 + 41 * rootWords;
        var random = new Random(seed);

        var operands = new List<(string Name, BigInteger Value)>(s_sqrtFloorKinds.Length + 50);
        foreach (var kind in s_sqrtFloorKinds)
        {
            operands.Add((kind, GpuBigIntSqrtTests.BuildSqrtFloorOperand(kind, rootWords)));
        }

        for (var i = 0; i < 50; i++)
        {
            var bitLength = (i & 1) == 0 ? 64 * rootWords : 64 * rootWords - 1;
            operands.Add(($"Rand{i}(seed={seed})", GpuBigIntSqrtTests.RandomWithBitLength(random, bitLength)));
        }

        var dHighBound = BigInteger.One << (16 * rootWords + 21);
        var stepBound = BigInteger.One << (32 * g.StepWords);

        foreach (var (name, a) in operands)
        {
            var trace = SqrtKarpMarksteinReference.Run(a, rootWords);
            var top = a >> (32 * (rootWords - 1));

            // K-P2: 0 <= floor(2^P / sqrt(A)) - y <= ApproximateInverseSqrtMaxError.
            var yFloor = InverseSqrtDoublingReference.FloorScaledInverseSqrt(top, g.P);
            var yGap = yFloor - trace.Y;
            Assert.True(yGap >= 0 && yGap <= GpuBigIntDivRem.ApproximateInverseSqrtMaxError,
                $"{name} n={rootWords}: floor(2^P/sqrt(A)) - y = {yGap}");

            // K-P3: buffer bounds for s0, d_hi, d', delta.
            var sqrtTop = GpuBigIntSqrtTests.FloorSqrt(top);
            Assert.True(trace.S0 <= sqrtTop, $"{name} n={rootWords}: s0 exceeds floor(sqrt(A))");
            Assert.Equal(top - trace.S0 * trace.S0, trace.DHigh);
            Assert.True(trace.DHigh.Sign >= 0 && trace.DHigh < dHighBound,
                $"{name} n={rootWords}: d_hi out of [0, 2^(16n+21))");
            Assert.True(trace.DShifted.Sign >= 0 && trace.DShifted < stepBound,
                $"{name} n={rootWords}: d' out of stepWords");
            Assert.True(trace.Delta.Sign >= 0 && trace.Delta < stepBound,
                $"{name} n={rootWords}: delta out of stepWords");

            // K-P4: floor(sqrt(a)) - r'' in {0, 1}.
            var estimateGap = trace.Root - trace.Estimate;
            Assert.True(estimateGap >= 0 && estimateGap <= 1,
                $"{name} n={rootWords}: floor(sqrt(a)) - r'' = {estimateGap}");
        }
    }
}
