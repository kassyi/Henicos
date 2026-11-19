#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class UnsupportedIntrinsicArgumentTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void MBarrierInit_ArgumentWrites_RunBeforeTheFailure(bool forceManaged)
    {
        var data = new[] { 0, 0, 0, 0 };
        var ex = RunAndRecord(
            forceManaged,
            () => UnsupportedIntrinsicArgumentKernels.LaunchSideEffectKernel(data, blockDimX: 1)
        );

        AssertExceptionType(forceManaged, ex);
        Assert.Equal([0, 1, 0, 0], data);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void MBarrierInit_TwoArguments_RunLeftToRight(bool forceManaged)
    {
        var data = new[] { 0, 0, 0, 0 };
        var ex = RunAndRecord(
            forceManaged,
            () => UnsupportedIntrinsicArgumentKernels.LaunchOrderKernel(data, blockDimX: 1)
        );

        AssertExceptionType(forceManaged, ex);
        Assert.Equal([2, 1, 2, 0], data);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvertToSharedMemory_Argument_RunsBeforeTheFailure(bool forceManaged)
    {
        var data = new[] { 0, 0, 0, 0 };
        var ex = RunAndRecord(
            forceManaged,
            () => UnsupportedIntrinsicArgumentKernels.LaunchValueKernel(data, blockDimX: 1)
        );

        AssertExceptionType(forceManaged, ex);
        Assert.Equal([1, 1, 0, 0], data);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void WmmaStore_Arguments_RunLeftToRightBeforeFailure(bool forceManaged)
    {
        var data = new[] { 0, 0, 0, 0 };
        var ex = RunAndRecord(
            forceManaged,
            () => UnsupportedIntrinsicArgumentKernels.LaunchWmmaStoreKernel(data, blockDimX: 1)
        );

        AssertExceptionType(forceManaged, ex);
        Assert.Equal([2, 1, 2, 0], data);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void MBarrierInit_ThrowingArgument_ThrowsItBeforeTheFailure(bool forceManaged)
    {
        var data = new[] { 0, 0, 0, 0 };
        var ex = RunAndRecord(
            forceManaged,
            () => UnsupportedIntrinsicArgumentKernels.LaunchThrowingArgumentKernel(data, blockDimX: 1)
        );

        Assert.IsType<DivideByZeroException>(ex);
    }

    private static void AssertExceptionType(bool forceManaged, Exception? ex)
    {
        if (forceManaged)
        {
            Assert.IsType<GpuOnlyException>(ex);
        }
        else
        {
            Assert.IsType<NotSupportedException>(ex);
        }
    }

    private Exception? RunAndRecord(bool forceManaged, Action launch)
    {
        Skip.If(IsCudaDialect, "CUDA implements the intrinsics.");
        return Record.Exception(() =>
        {
            using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
            {
                launch();
            }
        });
    }
}
