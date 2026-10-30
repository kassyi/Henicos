#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

/// <summary>
/// The operators of <c>GpuBigIntRef</c> on the managed fallback, which calls the library's targets as C# methods (#711).
/// The values are those of <see cref="BigIntRefOperatorTests"/>.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Stdlib)]
[Collection(TestCollections.GlobalStateSequential)]
public class BigIntRefOperatorManagedTests : TranspilerTestBase
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_ShiftOperators_ShiftTheLimbs()
    {
        var left = new uint[BigIntRefOperatorCases.Input.Length];
        var right = new uint[BigIntRefOperatorCases.Input.Length];

        ManagedKernelRun.Run(() => BigIntRefOperatorKernels.LaunchShiftKernel(BigIntRefOperatorCases.Input, left, right,
            BigIntRefOperatorCases.Input.Length, blockDimX: 1));

        Assert.Equal(BigIntRefOperatorCases.ShiftedLeft, left);
        Assert.Equal(BigIntRefOperatorCases.ShiftedRight, right);
    }
}
#endif
