using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

/// <summary>
/// The operators of <c>GpuBigIntRef</c> on the native target (CPU or CUDA). The managed fallback runs the same kernel in
/// <c>BigIntRefOperatorManagedTests</c>.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Stdlib)]
public class BigIntRefOperatorTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ShiftOperators_ShiftTheLimbs()
    {
        var left = new uint[BigIntRefOperatorCases.Input.Length];
        var right = new uint[BigIntRefOperatorCases.Input.Length];

        BigIntRefOperatorKernels.LaunchShiftKernel(BigIntRefOperatorCases.Input, left, right,
            BigIntRefOperatorCases.Input.Length, blockDimX: 1);

        Assert.Equal(BigIntRefOperatorCases.ShiftedLeft, left);
        Assert.Equal(BigIntRefOperatorCases.ShiftedRight, right);
    }

    // x = x << s writes the words it reads: each word must come from the unshifted value, as BigInteger computes it
    // from the input the test holds (also for a negative amount, which shifts the other way).
    // mutant: GpuBigIntKernels.cu.cs ShiftWordsLeft runs i upward -> shifts 3, 40, 71 and -40 (x >> 40) fail
    // mutant: ExecuteShiftLeft(ref ...) without the `amount < 0` branch -> -1, -40, -127 fail
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [MemberData(nameof(BigIntRefOperatorCases.Shifts), MemberType = typeof(BigIntRefOperatorCases))]
    public void ShiftOperators_ResultIsTheOperand_ShiftTheUnshiftedValue(int shiftBits)
    {
        var left = (uint[])BigIntRefOperatorCases.InPlaceInput.Clone();
        var right = (uint[])BigIntRefOperatorCases.InPlaceInput.Clone();

        BigIntRefOperatorKernels.LaunchInPlaceShiftKernel(left, right, left.Length, shiftBits, blockDimX: 1);

        Assert.Equal(BigIntRefOperatorCases.ExpectedShiftLeft(shiftBits), left);
        Assert.Equal(BigIntRefOperatorCases.ExpectedShiftRight(shiftBits), right);
    }
}

/// <summary>The input of <see cref="BigIntRefOperatorKernels"/> and the expected limbs, least significant first.</summary>
internal static class BigIntRefOperatorCases
{
    public static readonly uint[] Input = [0x80000001u, 0x00000003u];

    // 0x3_80000001 << 3 = 0x1C_00000008
    public static readonly uint[] ShiftedLeft = [0x00000008u, 0x0000001Cu];

    // 0x3_80000001 >> 3 = 0x70000000
    public static readonly uint[] ShiftedRight = [0x70000000u, 0x00000000u];

    // Every word non-zero and different, so a word read from the wrong place changes the result
    public static readonly uint[] InPlaceInput = [0x80000001u, 0x00000003u, 0xF0F0F0F0u, 0x12345678u];

    // Inside a word (3, 31), whole words (32), words and bits (40, 71), the top bit only (127), past the width (128,
    // int.MaxValue); a negative amount shifts the other way, as for BigInteger (int.MinValue moves every bit out)
    public static TheoryData<int> Shifts => [0, 3, 31, 32, 40, 71, 127, 128, int.MaxValue, -1, -40, -127, int.MinValue];

    public static uint[] ExpectedShiftLeft(int shiftBits) =>
        BigIntWords.ToWords(BigIntWords.ToBigInteger(InPlaceInput) << shiftBits, InPlaceInput.Length);

    public static uint[] ExpectedShiftRight(int shiftBits) =>
        BigIntWords.ToWords(BigIntWords.ToBigInteger(InPlaceInput) >> shiftBits, InPlaceInput.Length);
}
