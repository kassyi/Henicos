using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class CpuDialectLibraryCallTests : TranspilerTestBase
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
}
