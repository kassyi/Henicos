#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// The float vector add and subtract align the operand with the larger exponent by shifting it left into the headroom
/// of the result (one word more than the wider operand), and shift the other operand right for the rest of the exponent
/// difference. When the magnitudes are added, one bit of the headroom stays free for the carry of the sum.
/// </summary>
[Category(TestCategory.Unit)]
[TestTarget(ExecutionTarget.GpuExecution)]
public sealed class GpuBigFloatVectorAddAlignmentTests : IDisposable
{
    private readonly GpuKernelLoader.CudaContextScope _scope;
    private readonly GpuWorkspace _workspace;

    public GpuBigFloatVectorAddAlignmentTests()
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

    public enum Operation
    {
        ZipAdd,
        ZipSubtract,
        SelectAdd,
        SelectSubtract
    }

    // label, operation, left words, left exponent, left negative, right words, right exponent, right negative.
    // All-ones significands make every alignment carry through every word. h is the headroom of the operand with the
    // larger exponent: 32 * (result words - its words), the result having max(left, right) + 1 words.
    public static TheoryData<string, Operation, int, int, bool, int, int, bool> Rows => new()
    {
        // The exponent difference equals the headroom: shifting the left operand into all of it left no bit for the
        // carry (#1353).
        { "diff = h, left larger", Operation.ZipAdd, 2, 32, false, 2, 0, false },
        { "diff = h, right larger", Operation.ZipAdd, 2, 0, false, 2, 32, false },
        { "diff = h - 1, exact", Operation.ZipAdd, 2, 31, false, 2, 0, false },
        { "diff = h + 1", Operation.ZipAdd, 2, 33, false, 2, 0, false },
        { "diff = 2h", Operation.ZipAdd, 2, 64, false, 2, 0, false },
        { "diff = 2h, right larger", Operation.ZipAdd, 2, 0, false, 2, 64, false },
        { "diff = h, opposite signs", Operation.ZipAdd, 2, 32, false, 2, 0, true },
        { "diff = h, negative sum", Operation.ZipAdd, 2, 32, true, 2, 0, true },
        { "diff = h, subtract a negative", Operation.ZipSubtract, 2, 32, false, 2, 0, true },
        { "diff = h, subtract", Operation.ZipSubtract, 2, 32, false, 2, 0, false },
        { "1 and 2 words, diff = h = 64", Operation.ZipAdd, 1, 64, false, 2, 0, false },
        { "2 and 1 words, diff = h = 32", Operation.ZipAdd, 2, 32, false, 1, 0, false },
        { "2 and 1 words, right larger by 64", Operation.ZipAdd, 2, 0, false, 1, 64, false },
        // 33 words pass one warp, so the carry crosses a warp boundary on its way to the top word.
        { "33 words, diff = h", Operation.ZipAdd, 33, 32, false, 33, 0, false },
        { "scalar, diff = h", Operation.SelectAdd, 2, 32, false, 2, 0, false },
        { "scalar, scalar larger", Operation.SelectAdd, 2, 0, false, 2, 32, false },
        { "scalar subtract a negative, diff = h", Operation.SelectSubtract, 2, 32, false, 2, 0, true }
    };

    // Properties, with the exact sum computed from the inputs of the test (never from a value read back):
    // 1. The result is within one unit of its last place of the exact sum, |r - s| < 2^outExp: only the right shift of
    //    the operand with the smaller exponent drops bits, and they are below 2^outExp.
    // 2. The sign is the sign of the exact sum (zero has none to check).
    // 3. The alignment keeps all the headroom but the carry bit of a sum: outExp = max(eSmall, eLarge - (h - c)), with
    //    c = 1 when the magnitudes are added and 0 when they are subtracted (no carry), so a difference within h - c
    //    is exact.
    // mutant: GpuBigIntVectorKernels.AddSub.cu.cs `carryBits = ... ? 1 : 0` -> `carryBits = 0` (same-sign rows
    // "diff = h ...": the carry is lost, property 1)
    // mutant: `carryBits = ... ? 1 : 0` -> `carryBits = 1` (rows "opposite signs", "subtract": property 3)
    // mutant: `bitOffsetB = -(diff - shift)` -> `bitOffsetB = -diff` (rows "diff = 2h", "diff = h + 1": property 1)
    // mutant: `outExp = eB - shift` -> `outExp = eB` (rows "... right larger": properties 1 and 3)
    [Theory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(Rows))]
    public void GpuBigFloatArray_AddSubtract_KeepsTheCarryAndOneUlpOfTheExactValue(
        string label,
        Operation operation,
        int leftWords,
        int leftExponent,
        bool leftNegative,
        int rightWords,
        int rightExponent,
        bool rightNegative)
    {
        var leftMagnitude = (BigInteger.One << (32 * leftWords)) - 1;
        var rightMagnitude = (BigInteger.One << (32 * rightWords)) - 1;
        var subtract = operation is Operation.ZipSubtract or Operation.SelectSubtract;

        using var left = new GpuBigFloatArray(_workspace, 1, leftWords);
        using (var element = NewFloat(leftMagnitude, leftWords, leftExponent, leftNegative))
        {
            left.SetElement(0, element);
        }

        using var rightArray = new GpuBigFloatArray(_workspace, 1, rightWords);
        using var rightScalar = NewFloat(rightMagnitude, rightWords, rightExponent, rightNegative);
        rightArray.SetElement(0, rightScalar);

        using IGpuBigFloatVectorExpression expression = operation switch
        {
            Operation.ZipAdd => left.AsVectorizedEnumerable().ZipAdd(rightArray.AsVectorizedEnumerable()),
            Operation.ZipSubtract => left.AsVectorizedEnumerable().ZipSubtract(rightArray.AsVectorizedEnumerable()),
            Operation.SelectAdd => left.AsVectorizedEnumerable().SelectAdd(rightScalar),
            _ => left.AsVectorizedEnumerable().SelectSubtract(rightScalar)
        };
        var result = expression.Evaluate(_workspace);
        var resultWords = Math.Max(leftWords, rightWords) + 1;
        Assert.Equal(resultWords, result.WordsPerElement);

        var element0 = result.GetElement(0);
        var significand = element0.Significand.ToBigInteger();
        var outExp = element0.Exponent;
        var actual = BigFloatReference.Describe(significand, outExp, element0.IsNegative);

        var leftTerm = (leftNegative ? -leftMagnitude : leftMagnitude, (long)leftExponent);
        var rightSigned = rightNegative != subtract ? -rightMagnitude : rightMagnitude;
        var rightTerm = (rightSigned, (long)rightExponent);
        var resultTerm = (element0.IsNegative ? -significand : significand, (long)outExp);

        // Property 1: |r - s| < 2^outExp, as the signs of (s - r + 2^outExp) and (r - s + 2^outExp), both positive.
        var unit = (BigInteger.One, (long)outExp);
        var aboveLower = BigFloatReference.SignOfSum(leftTerm, rightTerm, (-resultTerm.Item1, resultTerm.Item2), unit);
        var belowUpper = BigFloatReference.SignOfSum((-leftTerm.Item1, leftTerm.Item2), (-rightSigned, rightTerm.Item2),
            resultTerm, unit);
        Assert.True(aboveLower > 0 && belowUpper > 0, $"{label}: {actual} is not within 2^{outExp} of the exact value");

        // Property 2: the sign of a nonzero result is the sign of the exact sum.
        var sign = BigFloatReference.SignOfSum(leftTerm, rightTerm);
        if (!significand.IsZero)
        {
            Assert.True(element0.IsNegative == sign < 0, $"{label}: sign of {actual}, expected {sign}");
        }

        // Property 3: the result exponent keeps every headroom bit, but the carry bit when the magnitudes are added.
        var leftLarger = leftExponent >= rightExponent;
        var largeExponent = leftLarger ? leftExponent : rightExponent;
        var smallExponent = leftLarger ? rightExponent : leftExponent;
        var headroom = 32 * (resultWords - (leftLarger ? leftWords : rightWords));
        var carryBits = leftNegative == rightSigned.Sign < 0 ? 1 : 0;
        Assert.Equal(Math.Max(smallExponent, largeExponent - (headroom - carryBits)), outExp);
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
