#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Runs <see cref="SharedMemoryAccessorKernels"/> on CUDA and on both CPU paths (#564): a struct whose getter and
/// setter each declare a <c>SharedMemory&lt;T&gt;</c> has one buffer per accessor, like a method.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class SharedMemoryAccessorTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Accessors_Execution_GiveEachAccessorItsOwnMemory(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryAccessorKernels.LaunchAccessorsKernel(output, blockDimX: 8));

        // Read: 100 + tid, Stored: 5 + 1
        Assert.Equal(Enumerable.Range(0, 8).Select(static tid => 106 + tid), output);
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
