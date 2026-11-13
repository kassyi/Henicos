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
public class SharedMemoryZeroSizeTests : TranspilerTestBase
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

    // The element 0 of an empty declaration is outside it. Only the managed path checks a SharedMemory<T> index today;
    // the native and CUDA paths do not check it for any count, which the bounds checks of #718 add
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void EmptyDeclaration_ReadElementZeroManaged_ThrowsIndexOutOfRangeException()
    {
        Skip.If(IsCudaDialect, "The managed execution path exists only on the CPU dialects.");
        var output = new int[1];

        Assert.Throws<IndexOutOfRangeException>(() =>
            Run(true, () => SharedMemoryZeroSizeKernels.LaunchReadEmptyKernel(output, blockDimX: 1)));
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
