using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// A <c>[GpuReadOnly]</c> array kept across a suspension (an argument temporary, a hoisted local) keeps its const
/// elements in the context, so that the native CPU build compiles, and every path reads the array.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class ReadOnlyArraySuspensionTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldArgument_ReadOnlyFieldPassedToSuspendingCall_CopiesTheElements(bool forceManaged)
    {
        int[] output = new int[4];
        var context = new ReadOnlySuspensionContext { Source = [3, 1, 4, 1], Output = output };

        Run(forceManaged, () => ReadOnlyArraySuspensionKernels.LaunchFieldArgumentKernel(context, blockDimX: 4));

        Assert.Equal([3, 1, 4, 1], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void HoistedLocal_ReadOnlyParameterHeldAcrossBarrier_CopiesTheElements(bool forceManaged)
    {
        int[] output = new int[4];

        Run(forceManaged, () => ReadOnlyArraySuspensionKernels.LaunchHoistedLocalKernel([5, 6, 7, 8], output, blockDimX: 4));

        Assert.Equal([5, 6, 7, 8], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void ChainedLocal_ReadOnlyFieldHeldThroughTwoLocals_AddsBothReads(bool forceManaged)
    {
        int[] output = new int[3];
        var context = new ReadOnlySuspensionContext { Source = [1, 2, 3], Output = output };

        Run(forceManaged, () => ReadOnlyArraySuspensionKernels.LaunchChainedLocalKernel(context, output, blockDimX: 3));

        Assert.Equal([2, 4, 6], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void WritableLocal_ArrayHeldAcrossBarrier_StaysWritable(bool forceManaged)
    {
        int[] data = [1, 2, 3, 4];

        Run(forceManaged, () => ReadOnlyArraySuspensionKernels.LaunchWritableLocalKernel(data, blockDimX: 4));

        Assert.Equal([2, 4, 6, 8], data);
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
