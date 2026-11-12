#nullable enable
using System.Collections.Immutable;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Device code calls <c>Slice</c>, <c>AsSpan</c>, <c>CopyTo</c> and <c>TryCopyTo</c> of <c>Memory&lt;T&gt;</c>,
/// <c>ReadOnlyMemory&lt;T&gt;</c>, <c>ArraySegment&lt;T&gt;</c> and <c>ImmutableArray&lt;T&gt;</c> as the members of
/// the same name of its array view, which check the bounds and return what C# returns. Each kernel runs on the managed
/// path, the native CPU build and CUDA, and gives the C# values.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class ArrayViewMemberTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void MemorySlice_OneAndTwoArguments_ReadsTheSlicedElements(bool forceManaged)
    {
        int[] values = [10, 20, 30, 40];
        int[] readOnly = [5, 6, 7];
        var r = new int[4];

        Run(forceManaged, () => ArrayViewMemberKernels.LaunchMemorySliceKernel(values, readOnly, r));

        Assert.Equal([20, 30, 7, 5], r);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void SegmentSlice_OneAndTwoArguments_ReadsTheSlicedElements(bool forceManaged)
    {
        var seg = new ArraySegment<int>([0, 10, 20, 30, 40], 1, 4);
        var r = new int[3];

        Run(forceManaged, () => ArrayViewMemberKernels.LaunchSegmentSliceKernel(seg, r));

        Assert.Equal([20, 30, 2], r);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmutableSliceAndAsSpan_TwoArguments_ReadsTheSlicedElements(bool forceManaged)
    {
        var imm = ImmutableArray.Create(10, 20, 30, 40);
        var r = new int[2];

        Run(forceManaged, () => ArrayViewMemberKernels.LaunchImmutableSliceKernel(imm, r));

        Assert.Equal([30, 20], r);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadOnlyMemoryCopyTo_LongerDestination_CopiesThePrefix(bool forceManaged)
    {
        int[] source = [7, 8];
        var destination = new int[3];

        Run(forceManaged, () => ArrayViewMemberKernels.LaunchReadOnlyMemoryCopyToKernel(source, destination));

        Assert.Equal([7, 8, 0], destination);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmutableCopyTo_SpanAndArray_CopiesThePrefix(bool forceManaged)
    {
        var imm = ImmutableArray.Create(5, 6);
        var toSpan = new int[4];
        var toArray = new int[3];

        Run(forceManaged, () => ArrayViewMemberKernels.LaunchImmutableCopyToKernel(imm, toSpan, toArray));

        Assert.Equal([5, 6, 0, 0], toSpan);
        Assert.Equal([5, 6, 0], toArray);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void SegmentCopyTo_ArrayAndSegment_CopiesThePrefix(bool forceManaged)
    {
        var seg = new ArraySegment<int>([1, 2, 3, 4], 1, 2);
        var toArray = new int[3];
        int[] backing = [9, 9, 9, 9];
        var toSegment = new ArraySegment<int>(backing, 1, 3);

        Run(forceManaged, () => ArrayViewMemberKernels.LaunchSegmentCopyToKernel(seg, toArray, toSegment));

        Assert.Equal([2, 3, 0], toArray);
        Assert.Equal([9, 2, 3, 9], backing);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void MemoryTryCopyTo_ShorterDestination_ReturnsFalseAndCopiesNothing(bool forceManaged)
    {
        int[] source = [1, 2, 3];
        int[] destination = [0, 0];
        var r = new int[2];

        Run(forceManaged, () => ArrayViewMemberKernels.LaunchMemoryTryCopyToKernel(source, destination, r));

        Assert.Equal([0, 1], r);
        Assert.Equal([2, 3], destination);
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
