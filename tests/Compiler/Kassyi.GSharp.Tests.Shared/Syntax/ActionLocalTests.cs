namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A local of type <c>Action</c> runs its lambda or local function on every execution path; before, the native CPU
/// build stopped on <c>System_Action</c> without its argument list.
/// </summary>
[Category(TestCategory.Syntax)]
public class ActionLocalTests : TranspilerTestBase
{
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ActionLocal_LambdaWithoutArguments_RunsTheLambda()
    {
        var dst = new int[4];

        ActionLocalKernels.LaunchActionLocalKernel(dst, blockDimX: 4);

        Assert.Equal([1, 2, 3, 4], dst);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ActionLocal_LocalFunctionGroup_RunsTheFunction()
    {
        var dst = new int[4];

        ActionLocalKernels.LaunchActionMethodGroupKernel(dst, blockDimX: 4);

        Assert.Equal([3, 4, 5, 6], dst);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ActionLocal_CalledAfterABarrier_RunsTheLambda()
    {
        var dst = new int[4];

        ActionLocalKernels.LaunchActionAcrossBarrierKernel(dst, blockDimX: 4);

        Assert.Equal([1, 1, 1, 1], dst);
    }
}
