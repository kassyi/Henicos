#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Runs <see cref="SharedMemoryZeroSizeKernels"/> on CUDA and on both CPU paths, native and managed (#1790). CUDA has
/// no array of 0 elements, so the compiler declares one element there; the declaration still holds no element that C#
/// can reach.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public partial class SharedMemoryZeroSizeTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyDeclaration_BesideUsedDeclaration_RunsAndKeepsTheOtherMemory(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[4];

        Run(forceManaged, () => SharedMemoryZeroSizeKernels.LaunchEmptyBesideUsedKernel(output, blockDimX: 4));

        Assert.Equal([20 + 0, 30 + 1, 40 + 2, 10 + 3], output);
    }

    // The managed path exists only on the CPU dialects
    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}
