#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Runs <see cref="SharedMemoryLocalFormsKernels"/> on CUDA and on both CPU paths (#1085): a
/// <c>SharedMemory&lt;T&gt;</c> local that is declared by a parenthesized creation, by a copy of another local or by
/// the result of a device function is declared in the generated code like one declared by a direct <c>new</c>.
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

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void CreationInReturn_Execution_DeclaresTheMemoryInTheFunction(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryLocalFormsKernels.LaunchCreationInReturnKernel(output, blockDimX: 8));

        Assert.Equal([30, 31, 32, 33, 34, 35, 36, 37], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void CreationInExpressionBody_Execution_DeclaresTheMemoryInTheFunction(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged,
            () => SharedMemoryLocalFormsKernels.LaunchCreationInExpressionBodyKernel(output, blockDimX: 8));

        Assert.Equal([40, 41, 42, 43, 44, 45, 46, 47], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void CreationInLocalFunctionExpressionBody_Execution_DeclaresTheMemoryInTheFunction(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryLocalFormsKernels.LaunchCreationInLocalFunctionExpressionBodyKernel(
            output, blockDimX: 8));

        Assert.Equal([50, 51, 52, 53, 54, 55, 56, 57], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void CreationInArgument_Execution_DeclaresTheMemoryBeforeTheStatement(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryLocalFormsKernels.LaunchCreationInArgumentKernel(output, blockDimX: 8));

        Assert.Equal([50, 51, 52, 53, 54, 55, 56, 57], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void CreationInConditional_Execution_DeclaresTheMemoryBeforeTheStatement(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryLocalFormsKernels.LaunchCreationInConditionalKernel(output, blockDimX: 8));

        Assert.Equal([60, 61, 62, 63, 64, 65, 66, 67], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void BarrierFunctionResult_Execution_KeepsTheResultAcrossTheBarrier(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryLocalFormsKernels.LaunchBarrierFunctionResultKernel(output, blockDimX: 8));

        Assert.Equal([101, 102, 103, 104, 105, 106, 107, 100], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void CallResultAcrossBarrier_Execution_SeesTheOtherThreadsWrites(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged,
            () => SharedMemoryLocalFormsKernels.LaunchCallResultAcrossBarrierKernel(output, blockDimX: 8));

        Assert.Equal([111, 112, 113, 114, 115, 116, 117, 110], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalOfLocals_Execution_SharesTheChosenMemory(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryLocalFormsKernels.LaunchConditionalOfLocalsKernel(output, blockDimX: 8));

        Assert.Equal([70, 71, 72, 73, 74, 75, 76, 77], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void DeviceFunctionTwice_Execution_SharesTheMemory(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryLocalFormsKernels.LaunchDeviceFunctionTwiceKernel(output, blockDimX: 8));

        Assert.Equal([160, 162, 164, 166, 168, 170, 172, 174], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void AliasAcrossBarrier_Execution_SeesTheOtherThreadsWrites(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemoryLocalFormsKernels.LaunchAliasAcrossBarrierKernel(output, blockDimX: 8));

        Assert.Equal([91, 92, 93, 94, 95, 96, 97, 90], output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void FloatAndByteAlias_Execution_UseTheElementType(bool forceManaged)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var floats = new float[8];
        var bytes = new byte[8];

        Run(forceManaged,
            () => SharedMemoryLocalFormsKernels.LaunchFloatAndByteAliasKernel(floats, bytes, blockDimX: 8));

        Assert.Equal([0.5f, 1.5f, 2.5f, 3.5f, 4.5f, 5.5f, 6.5f, 7.5f], floats);
        Assert.Equal([3, 4, 5, 6, 7, 8, 9, 10], bytes);
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
