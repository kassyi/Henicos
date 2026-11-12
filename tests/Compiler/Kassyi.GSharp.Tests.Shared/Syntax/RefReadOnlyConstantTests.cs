#nullable enable
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Runs <see cref="RefReadOnlyConstantKernels"/> on the native or CUDA path and on the managed path, whose copy of the
/// kernel passes the constant without <c>in</c>.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class RefReadOnlyConstantTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstantLocalToRefReadOnly_AnyPath_ReadsTheConstant(bool forceManaged)
    {
        int[] r = [0, 0];

        Run(forceManaged, () => RefReadOnlyConstantKernels.LaunchConstantLocalToRefReadOnlyKernel(r, blockDimX: 2));

        Assert.Equal([3, 3], r);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstantFieldToRefReadOnly_AnyPath_ReadsTheConstant(bool forceManaged)
    {
        int[] r = [0, 0];

        Run(forceManaged, () => RefReadOnlyConstantKernels.LaunchConstantFieldToRefReadOnlyKernel(r, blockDimX: 2));

        Assert.Equal([4, 4], r);
    }

    // The managed path exists only on the CPU dialects
    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }
}
