using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// <c>nameof</c> in device code is the constant C# gives it: its length and the comparisons of constant strings are
/// written as their values. Each kernel runs on the managed path, the native CPU build and CUDA, and gives the C#
/// values.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class NameofTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void NameLength_InDeviceFunction_AddsTheLengthOfTheName(bool forceManaged)
    {
        int[] inputs = [0, 5, -13, 100];
        var r = new int[4];

        Run(forceManaged, () => NameofKernels.LaunchNameLengthKernel(inputs, r, blockDimX: 4));

        Assert.Equal([13, 18, 0, 113], r);
        Assert.Equal(nameof(NameofKernels).Length, r[0]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void NameComparison_ConstantStrings_WritesTheValuesCSharpGives(bool forceManaged)
    {
        var r = new int[6];

        Run(forceManaged, () => NameofKernels.LaunchNameComparisonKernel(r, blockDimX: 1));

        Assert.Equal([1, 0, 1, 3, 5, 3], r);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void NameLength_AfterBarrier_AddsTheLengthOfTheName(bool forceManaged)
    {
        var r = new int[4];

        Run(forceManaged, () => NameofKernels.LaunchNameAfterBarrierKernel(r, blockDimX: 4));

        Assert.Equal([22, 23, 24, 25], r);
    }

    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}
