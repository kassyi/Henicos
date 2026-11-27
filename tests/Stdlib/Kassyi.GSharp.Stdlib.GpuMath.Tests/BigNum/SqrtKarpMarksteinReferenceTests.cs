#nullable enable
using Geometry = Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.InverseSqrtDoublingReference.Geometry;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

// Properties checked here (property-test-design §1):
// - R-P1: the C# BigInteger port agrees bit for bit with every golden case from the independent Python model.
// - R-P2: floor(InitialGuess(A, B) / 2^(E - 64)) == InitialGuess(A, 16w + 62), and the one-sync ReadLeadingBits
//   window produces the exact same firstY.
// - K-P2, K-P3, K-P4: across structured and random operands on the CPU model, 0 <= floor(2^P / sqrt(A)) - y <= 3,
//   every intermediate value fits its buffer bound, and floor(sqrt(a)) - r'' in {0, 1}.
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
public class SqrtKarpMarksteinReferenceTests
{
    private static readonly int[] s_modelRootWordCounts =
        [1, 2, 3, 4, 5, 6, 7, 8, 9, 15, 16, 17, 31, 32, 33, 45, 46, 47, 64, 65];

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
        SqrtKarpMarksteinReference.AssertTraceEquals(golden.Trace, actual, $"{label} n={rootWords}");
    }

    /// <summary>Operand word count <c>w</c> and case index (0..22) for the first-stage initial-guess identity.</summary>
    public static TheoryData<int, int> FirstYInputs()
    {
        var data = new TheoryData<int, int>();
        for (var w = 2; w <= 40; w++)
        {
            for (var caseIndex = 0; caseIndex < 23; caseIndex++)
            {
                data.Add(w, caseIndex);
            }
        }

        foreach (var w in new[] { 47, 101 })
        {
            for (var caseIndex = 0; caseIndex < 23; caseIndex++)
            {
                data.Add(w, caseIndex);
            }
        }

        return data;
    }

    // R-P2: floor(InitialGuess(A, B) / 2^(E - 64)) == InitialGuess(A, 16w + 62), and the ReadLeadingBits window gives
    // the same first Y bit for bit.
    // mutant: GpuBigIntDivRem.cs:818 `(actualBits & 1) == 0 ? 2 : 3` -> `(actualBits & 1) == 0 ? 3 : 2`
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(FirstYInputs))]
    public void SqrtKarpMarksteinReference_FirstY_EqualsTheShiftedInitialGuess(int operandWords, int caseIndex)
    {
        var g = new Geometry(operandWords - 1);
        var a = BuildFirstYOperand(operandWords, caseIndex);
        var shifted = InverseSqrtDoublingReference.FirstY(a, g);
        var direct = InverseSqrtDoublingReference.InitialGuess(a, 16 * operandWords + 62);
        Assert.True(shifted == direct,
            $"w={operandWords} case={caseIndex}: FirstY ({shifted:X}) != direct ({direct:X})");

        var actualBits = (int)a.GetBitLength();
        var leadingBits = actualBits >= 64 ? (ulong)(a >> (actualBits - 64)) : (ulong)(a << (64 - actualBits));
        var fromLeading = GpuBigIntDivRem.InitialGuessSquareRootFromLeadingBits(
            actualBits, leadingBits, 16 * operandWords + 62);
        Assert.True(direct == fromLeading,
            $"w={operandWords} case={caseIndex}: direct ({direct:X}) != ReadLeadingBits ({fromLeading:X})");
    }

    /// <summary>Structured operands for K-P2..K-P4 on the CPU reference model.</summary>
    public static TheoryData<int, string> ModelStructuredInputs()
    {
        var data = new TheoryData<int, string>();
        foreach (var n in s_modelRootWordCounts)
        {
            foreach (var kind in GpuBigIntSqrtTests.SqrtFloorOperandKinds)
            {
                data.Add(n, kind);
            }
        }

        return data;
    }

    // K-P2, K-P3, K-P4 on the CPU reference model across structured operands.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ModelStructuredInputs))]
    public void SqrtKarpMarksteinReference_StructuredOperand_LandsOnTheFloorOrOneBelow(int rootWords, string kind)
    {
        var a = GpuBigIntSqrtTests.BuildSqrtFloorOperand(kind, rootWords);
        var trace = SqrtKarpMarksteinReference.Run(a, rootWords);
        SqrtKarpMarksteinReference.AssertTraceProperties(a, rootWords, trace, $"{kind} n={rootWords}");
    }

    /// <summary>Seeded random operands for K-P2..K-P4 on the CPU reference model.</summary>
    public static TheoryData<int, int> ModelRandomInputs()
    {
        var data = new TheoryData<int, int>();
        foreach (var n in s_modelRootWordCounts)
        {
            for (var sampleIndex = 0; sampleIndex < 50; sampleIndex++)
            {
                data.Add(n, sampleIndex);
            }
        }

        return data;
    }

    // K-P2, K-P3, K-P4 on the CPU reference model across seeded random operands of both bit lengths.
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(ModelRandomInputs))]
    public void SqrtKarpMarksteinReference_RandomOperand_LandsOnTheFloorOrOneBelow(int rootWords, int sampleIndex)
    {
        var seed = 5113 + 41 * rootWords + 997 * sampleIndex;
        var bitLength = (sampleIndex & 1) == 0 ? 64 * rootWords : 64 * rootWords - 1;
        var a = GpuBigIntSqrtTests.RandomWithBitLength(new Random(seed), bitLength);
        var trace = SqrtKarpMarksteinReference.Run(a, rootWords);
        SqrtKarpMarksteinReference.AssertTraceProperties(
            a, rootWords, trace, $"Rand{sampleIndex}(seed={seed}) n={rootWords}");
    }

    private static BigInteger BuildFirstYOperand(int operandWords, int caseIndex)
    {
        var bits = 32 * operandWords;
        if (caseIndex == 0)
        {
            return BigInteger.One << (bits - 2);
        }

        if (caseIndex == 1)
        {
            return (BigInteger.One << bits) - 1;
        }

        if (caseIndex == 2)
        {
            return BigInteger.One << (bits - 1);
        }

        var randomIndex = caseIndex - 3;
        var seed = 6007 + 31 * operandWords + 991 * randomIndex;
        var bitLength = (randomIndex & 1) == 0 ? bits : bits - 1;
        return GpuBigIntSqrtTests.RandomWithBitLength(new Random(seed), bitLength);
    }
}
