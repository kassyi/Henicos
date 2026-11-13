using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// A zero-length stack span passed to a StackLinq call that is not fused builds and gives the C# result on every path.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class StackLinqEmptySpanTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void StaticForm_EmptyStackSpan_CountsNothing(bool forceManaged)
    {
        int[] results = [-1];

        Run(forceManaged, () => StackLinqEmptySpanKernels.LaunchStaticFormKernel(0, results));

        Assert.Equal([7], results);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReducedForm_EmptyStackSpan_CountsNothing(bool forceManaged)
    {
        int[] results = [-1];

        Run(forceManaged, () => StackLinqEmptySpanKernels.LaunchReducedFormKernel(0, results));

        Assert.Equal([7], results);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void StaticForm_NonEmptyStackSpan_CountsTheElementsAfterTheFirst(bool forceManaged)
    {
        int[] results = [-1];

        Run(forceManaged, () => StackLinqEmptySpanKernels.LaunchNonEmptyStaticFormKernel(6, results));

        Assert.Equal([8], results);
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
