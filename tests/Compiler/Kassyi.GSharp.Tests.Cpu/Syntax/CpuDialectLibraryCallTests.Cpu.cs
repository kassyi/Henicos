#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class CpuDialectLibraryCallTests
{
    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 3)]
    [InlineData(true, -1)]
    [InlineData(true, 3)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void ElementAt_IndexOutOfRange_ThrowsArgumentOutOfRangeException(bool forceManaged, int index)
    {
        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            int[] data = [7, 8, 9];
            int[] result = [0];

            Assert.Throws<ArgumentOutOfRangeException>(
                () => CpuDialectLibraryCallKernels.LaunchElementAtIndexKernel(data, index, result));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void First_UnfusedEmptySequence_ThrowsInvalidOperationException(bool forceManaged)
    {
        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            int[] data = [7, 8, 9];
            int[] result = [0];

            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchUnfusedFirstKernel(data, 0, result));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Last_UnfusedEmptySequence_ThrowsInvalidOperationException(bool forceManaged)
    {
        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            int[] data = [7, 8, 9];
            int[] result = [0];

            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchUnfusedLastKernel(data, 0, result));
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Single_InvalidElementCount_ThrowsInvalidOperationException(bool forceManaged, int takeCount)
    {
        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            int[] data = [7, 8, 9];
            int[] result = [0];

            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchUnfusedSingleKernel(data, takeCount, result));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void SingleOrDefault_MultipleElements_ThrowsInvalidOperationException(bool forceManaged)
    {
        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            int[] data = [7, 8, 9];
            int[] result = [0];

            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchUnfusedSingleOrDefaultKernel(data, 2, result));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void TerminalOperations_FusedAndUnfusedEmptySequence_ThrowInvalidOperationException(bool forceManaged)
    {
        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            int[] data = [7, 8, 9];
            int[] intResult = [0];
            double[] doubleResult = [0.0];

            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchFusedFirstKernel(data, 0, intResult));
            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchFusedFirstPredicateKernel(data, 100, intResult));
            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchFusedLastKernel(data, 0, intResult));
            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchFusedLastPredicateKernel(data, 100, intResult));
            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchFusedMaxKernel(data, 0, intResult));
            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchFusedMinKernel(data, 0, intResult));
            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchFusedAverageKernel(data, 0, doubleResult));
            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchFusedSeedlessAggregateKernel(data, 0, intResult));

            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchUnfusedMaxKernel(data, 0, intResult));
            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchUnfusedMinKernel(data, 0, intResult));
            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchUnfusedAverageKernel(data, 0, doubleResult));
            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchUnfusedSeedlessAggregateKernel(data, 0, intResult));
        }
    }
}
