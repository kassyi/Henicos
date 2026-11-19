#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Executes <see cref="SharedMemoryOutKernels"/> across CUDA and both CPU paths for inline <c>out</c> declarations
/// and discard <c>out</c> arguments of <c>SharedMemory&lt;T&gt;</c>.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class SharedMemoryOutTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void OutVar_SharedMemoryOutArgument_WritesThroughAliasedBuffer(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryOutKernels.LaunchOutVarKernel(output, blockDimX: 8));

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void OutTyped_ExplicitSharedMemoryOutArgument_WritesThroughAliasedBuffer(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryOutKernels.LaunchOutTypedKernel(output, blockDimX: 8));

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void OutDiscard_SharedMemoryOutDiscard_CompilesAndWritesSharedMemory(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryOutKernels.LaunchOutDiscardKernel(output, blockDimX: 8));

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], output);
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
