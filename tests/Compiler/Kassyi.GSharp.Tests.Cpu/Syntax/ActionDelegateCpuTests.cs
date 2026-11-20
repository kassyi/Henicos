using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// An <c>Action</c> as the parameter of a device function and a reassigned <c>Action</c> local, on the native CPU build
/// and on the managed path.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class ActionDelegateCpuTests : TranspilerTestBase
{
    [Theory]
    [InlineData(KernelExecution.Default)]
    [InlineData(KernelExecution.Managed)]
    [Scenario(TestScenario.HappyPath)]
    public void ActionParameter_DeviceFunction_RunsTheLambda(KernelExecution execution)
    {
        var dst = new int[4];

        using (GpuExecution.Use(execution))
        {
            ActionDelegateCpuKernels.LaunchActionParameterKernel(dst, blockDimX: 4);
        }

        Assert.Equal([2, 3, 4, 5], dst);
    }

    [Theory]
    [InlineData(KernelExecution.Default)]
    [InlineData(KernelExecution.Managed)]
    [Scenario(TestScenario.Boundary)]
    public void ActionLocal_AssignedAnotherLambda_RunsTheLastOne(KernelExecution execution)
    {
        var dst = new int[4];

        using (GpuExecution.Use(execution))
        {
            ActionDelegateCpuKernels.LaunchActionReassignedKernel(dst, blockDimX: 4);
        }

        Assert.Equal([1, 7, 1, 1], dst);
    }
}
