#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Runs <see cref="SharedMemoryLocalFormsKernels"/> on CUDA and on both CPU paths (#1085): a <c>SharedMemory&lt;T&gt;</c>
/// local that is declared by a parenthesized creation, by a copy of another local or by the result of a device
/// function is declared in the generated code like one declared by a direct <c>new</c>.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class SharedMemoryLocalFormsTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ParenthesizedCreation_Execution_DeclaresTheLocal(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryLocalFormsKernels.LaunchParenthesizedCreationKernel(output, blockDimX: 8));

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyOfLocal_Execution_SharesTheMemory(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryLocalFormsKernels.LaunchCopyOfLocalKernel(output, blockDimX: 8));

        Assert.Equal([10, 11, 12, 13, 14, 15, 16, 17], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void DeviceFunctionResult_Execution_SharesTheMemory(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryLocalFormsKernels.LaunchDeviceFunctionResultKernel(output, blockDimX: 8));

        Assert.Equal([20, 21, 22, 23, 24, 25, 26, 27], output);
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
