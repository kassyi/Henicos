using System;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public class CpuDialectLibraryCallTests : TranspilerTestBase
{
    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void ElementAt_UnfusedStackLinqCall_ReturnsElementAtTargetIndex()
    {
        int[] data = [7, 8, 9];
        int[] result = [0];

        CpuDialectLibraryCallKernels.LaunchElementAtKernel(data, result);

        Assert.Equal(8, result[0]);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Sort_MemoryExtensionsSpan_SortsArrayInPlace()
    {
        int[] data = [3, 1, 2];

        CpuDialectLibraryCallKernels.LaunchSortKernel(data);

        Assert.Equal([1, 2, 3], data);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void SelectAt_RepeatedCurrentAccess_DoesNotAdvanceIndex()
    {
        int[] data = [1, 2, 3, 4];
        int[] result = [0];

        CpuDialectLibraryCallKernels.LaunchSelectAtRepeatedCurrentKernel(data, result);

        Assert.Equal(32, result[0]);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void TerminalOperations_EmptySequenceNonTrapping_ReturnDotNetDefaults()
    {
        int[] data = [7, 8, 9];
        int[] results = new int[12];

        CpuDialectLibraryCallKernels.LaunchEmptySequenceBoundaryKernel(data, results);

        Assert.Equal([0, 0, 0, 1, 0, 0, 0, 0, 0, 42, 0, 42], results);
    }

    [SkippableTheory]
    [InlineData(false, -1)]
    [InlineData(false, 3)]
    [InlineData(true, -1)]
    [InlineData(true, 3)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void ElementAt_IndexOutOfRange_ThrowsArgumentOutOfRangeException(bool forceManaged, int index)
    {
        Skip.If(IsCudaDialect, "Trap exception propagation is verified on CPU execution paths.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            int[] data = [7, 8, 9];
            int[] result = [0];

            Assert.Throws<ArgumentOutOfRangeException>(
                () => CpuDialectLibraryCallKernels.LaunchElementAtIndexKernel(data, index, result));
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void First_UnfusedEmptySequence_ThrowsInvalidOperationException(bool forceManaged)
    {
        Skip.If(IsCudaDialect, "Trap exception propagation is verified on CPU execution paths.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            int[] data = [7, 8, 9];
            int[] result = [0];

            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchUnfusedFirstKernel(data, 0, result));
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Last_UnfusedEmptySequence_ThrowsInvalidOperationException(bool forceManaged)
    {
        Skip.If(IsCudaDialect, "Trap exception propagation is verified on CPU execution paths.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            int[] data = [7, 8, 9];
            int[] result = [0];

            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchUnfusedLastKernel(data, 0, result));
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }

    [SkippableTheory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Single_InvalidElementCount_ThrowsInvalidOperationException(bool forceManaged, int takeCount)
    {
        Skip.If(IsCudaDialect, "Trap exception propagation is verified on CPU execution paths.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            int[] data = [7, 8, 9];
            int[] result = [0];

            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchUnfusedSingleKernel(data, takeCount, result));
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void SingleOrDefault_MultipleElements_ThrowsInvalidOperationException(bool forceManaged)
    {
        Skip.If(IsCudaDialect, "Trap exception propagation is verified on CPU execution paths.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            int[] data = [7, 8, 9];
            int[] result = [0];

            Assert.Throws<InvalidOperationException>(
                () => CpuDialectLibraryCallKernels.LaunchUnfusedSingleOrDefaultKernel(data, 2, result));
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void TerminalOperations_FusedAndUnfusedEmptySequence_ThrowInvalidOperationException(bool forceManaged)
    {
        Skip.If(IsCudaDialect, "Trap exception propagation is verified on CPU execution paths.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
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
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }
}
