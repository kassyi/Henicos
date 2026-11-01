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
    public void Test_ShiftOperators_ShiftTheLimbs()
    {
        var left = new uint[BigIntRefOperatorCases.Input.Length];
        var right = new uint[BigIntRefOperatorCases.Input.Length];

        BigIntRefOperatorKernels.LaunchShiftKernel(BigIntRefOperatorCases.Input, left, right,
            BigIntRefOperatorCases.Input.Length, blockDimX: 1);

        Assert.Equal(BigIntRefOperatorCases.ShiftedLeft, left);
        Assert.Equal(BigIntRefOperatorCases.ShiftedRight, right);
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
}
