using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A local of type <c>Action</c> runs its lambda or local function on every execution path; before, the native CPU
/// build stopped on <c>System_Action</c> without its argument list.
/// </summary>
[Category(TestCategory.Syntax)]
public class ActionLocalTests : TranspilerTestBase
{
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ActionLocal_LambdaWithoutArguments_RunsTheLambda(bool forceManaged)
    {
        var dst = new int[4];

        Run(forceManaged, () => ActionLocalKernels.LaunchActionLocalKernel(dst, blockDimX: 4));

        Assert.Equal([1, 2, 3, 4], dst);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ActionLocal_LocalFunctionGroup_RunsTheFunction(bool forceManaged)
    {
        var dst = new int[4];

        Run(forceManaged, () => ActionLocalKernels.LaunchActionMethodGroupKernel(dst, blockDimX: 4));

        Assert.Equal([3, 4, 5, 6], dst);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void ActionLocal_CalledAfterABarrier_RunsTheLambda(bool forceManaged)
    {
        var dst = new int[4];

        Run(forceManaged, () => ActionLocalKernels.LaunchActionAcrossBarrierKernel(dst, blockDimX: 4));

        Assert.Equal([1, 1, 1, 1], dst);
    }

    // The Action with a type argument is not the one without: System_Action<int> keeps its argument
    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void ActionLocal_WithATypeArgument_PassesTheArgument(bool forceManaged)
    {
        var dst = new int[4];

        Run(forceManaged, () => ActionLocalKernels.LaunchActionOfIntLocalKernel(dst, blockDimX: 4));

        Assert.Equal([1, 2, 3, 4], dst);
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
