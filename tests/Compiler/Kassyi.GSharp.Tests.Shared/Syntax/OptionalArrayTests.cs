using System;
using System.Collections.Immutable;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class OptionalArrayTests : TranspilerTestBase
{
    [Scenario(TestScenario.HappyPath)]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_IsNullBiasKernel_WithNonNullAndNull_ShouldExecuteCorrectly(bool forceManaged)
    {
        float[] x = [1f, 2f];
        Run(forceManaged, () => OptionalArrayKernels.LaunchIsNullBiasKernel(x, [10f, 20f], blockDimX: 2));
        Assert.Equal([11f, 22f], x);

        x = [1f, 2f];
        Run(forceManaged, () => OptionalArrayKernels.LaunchIsNullBiasKernel(x, null, blockDimX: 2));
        Assert.Equal([1f, 2f], x);
    }

    [Scenario(TestScenario.Boundary)]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_IsNullBiasKernel_WithEmptyBias_ShouldTreatAsNull(bool forceManaged)
    {
        float[] x = [1f, 2f];
        Run(forceManaged, () => OptionalArrayKernels.LaunchIsNullBiasKernel(x, [], blockDimX: 2));
        Assert.Equal([1f, 2f], x);
    }

    [Scenario(TestScenario.HappyPath)]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_EqualsNullBiasKernel_WithNullAndNonNull_ShouldExecuteCorrectly(bool forceManaged)
    {
        float[] x = [1f, 2f];
        Run(forceManaged, () => OptionalArrayKernels.LaunchEqualsNullBiasKernel(x, null, blockDimX: 2));
        Assert.Equal([-1f, -1f], x);

        x = [1f, 2f];
        Run(forceManaged, () => OptionalArrayKernels.LaunchEqualsNullBiasKernel(x, [10f, 20f], blockDimX: 2));
        Assert.Equal([10f, 20f], x);
    }

    [Scenario(TestScenario.HappyPath)]
    [Fact]
    public void Test_MemoryAddKernel_ShouldModifyBackingArray()
    {
        float[] xArray = [1f, 2f];
        float[] yArray = [10f, 20f];
        OptionalArrayKernels.LaunchMemoryAddKernel(xArray.AsMemory(), yArray.AsMemory(), blockDimX: 2);
        Assert.Equal([11f, 22f], xArray);
    }

    [Scenario(TestScenario.Boundary)]
    [Fact]
    public void Test_SegmentScaleKernel_WithOffset_ShouldHonorOffsetAndCount()
    {
        float[] backing = [0f, 1f, 2f, 3f];
        var segment = new ArraySegment<float>(backing, 1, 2);
        OptionalArrayKernels.LaunchSegmentScaleKernel(segment, blockDimX: 2);
        Assert.Equal([0f, 4f, 6f, 3f], backing);
    }

    [Scenario(TestScenario.HappyPath)]
    [Fact]
    public void Test_ImmutableAddKernel_ShouldExecuteAndLeaveImmutableArrayUnchanged()
    {
        float[] x = [1f, 2f];
        var y = ImmutableArray.Create(10f, 20f);
        OptionalArrayKernels.LaunchImmutableAddKernel(x, y, blockDimX: 2);
        Assert.Equal([11f, 22f], x);
        // [WHY] Compare elements: a collection expression here would be target-typed to ImmutableArray<float>, whose Equals compares the underlying array reference.
        Assert.Equal([10f, 20f], y.AsSpan().ToArray());
    }

    [Scenario(TestScenario.HappyPath)]
    [Fact]
    public void Test_OptionalMemoryKernel_WithNullDefaultAndValue_ShouldExecuteCorrectly()
    {
        float[] x = [1f, 2f];
        OptionalArrayKernels.LaunchOptionalMemoryKernel(x, null, blockDimX: 2);
        Assert.Equal([1f, 2f], x);

        x = [1f, 2f];
        OptionalArrayKernels.LaunchOptionalMemoryKernel(x, default(Memory<float>), blockDimX: 2);
        Assert.Equal([1f, 2f], x);

        x = [1f, 2f];
        OptionalArrayKernels.LaunchOptionalMemoryKernel(x, new float[] { 10f, 20f }.AsMemory(), blockDimX: 2);
        Assert.Equal([11f, 22f], x);
    }

    [Scenario(TestScenario.HappyPath)]
    [Fact]
    public void Test_OptionalSegmentKernel_WithNullAndValue_ShouldExecuteCorrectly()
    {
        float[] x = [1f, 2f];
        OptionalArrayKernels.LaunchOptionalSegmentKernel(x, null, blockDimX: 2);
        Assert.Equal([1f, 2f], x);

        x = [1f, 2f];
        OptionalArrayKernels.LaunchOptionalSegmentKernel(x, new ArraySegment<float>([10f, 20f]), blockDimX: 2);
        Assert.Equal([11f, 22f], x);
    }

    [Scenario(TestScenario.SadPath)]
    [Fact]
    public void Test_IsNullBiasKernel_NullX_ShouldThrowArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            // Intentionally passing null to assert ArgumentNullException guard; this is the only allowed !.
            OptionalArrayKernels.LaunchIsNullBiasKernel(null!, [10f, 20f], blockDimX: 2));
        Assert.Equal("x", ex.ParamName);
    }

    [Scenario(TestScenario.SadPath)]
    [Fact]
    public void Test_IsNullBiasKernel_EmptyX_ShouldThrowArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            OptionalArrayKernels.LaunchIsNullBiasKernel([], [10f, 20f], blockDimX: 2));
        Assert.Equal("x", ex.ParamName);
    }

    [Scenario(TestScenario.SadPath)]
    [Fact]
    public void Test_MemoryAddKernel_EmptyX_ShouldThrowArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            OptionalArrayKernels.LaunchMemoryAddKernel(Memory<float>.Empty, new float[] { 10f, 20f }, blockDimX: 2));
        Assert.Equal("x", ex.ParamName);
    }

    [Scenario(TestScenario.SadPath)]
    [Fact]
    public void Test_ScaleKernel_EmptyValues_ShouldThrowArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            OptionalArrayKernels.LaunchScaleKernel([], blockDimX: 2));
        Assert.Equal("values", ex.ParamName);
    }

    // A null optional array and an empty one are the same on the device (its data pointer is null and its length 0), also
    // on the managed fallback (#806)
    [Scenario(TestScenario.Boundary)]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_LengthOptionalKernel_NullAndEmptyBias_ReadLengthZero(bool forceManaged)
    {
        var o = new[] { -1, -1 };
        Run(forceManaged, () => OptionalArrayKernels.LaunchLengthOptionalKernel(o, null));
        Assert.Equal([0, -1], o);

        o = [-1, -1];
        Run(forceManaged, () => OptionalArrayKernels.LaunchLengthOptionalKernel(o, []));
        Assert.Equal([0, -1], o);

        o = [-1, -1];
        Run(forceManaged, () => OptionalArrayKernels.LaunchLengthOptionalKernel(o, [1f, 2f, 3f]));
        Assert.Equal([3, -1], o);
    }

    // The kernel writes an optional [Out] / [In, Out] array only when it is there; null reaches it as null on every path
    [Scenario(TestScenario.Boundary)]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_WriteOptionalKernels_NullAndValue_AgreeOnEveryPath(bool forceManaged)
    {
        var o = new[] { -1 };
        Run(forceManaged, () => OptionalArrayKernels.LaunchOutOptionalKernel(null, o));
        Assert.Equal([0], o);
        Run(forceManaged, () => OptionalArrayKernels.LaunchInOutOptionalKernel(null, o));
        Assert.Equal([0], o);

        float[] bias = [1f];
        Run(forceManaged, () => OptionalArrayKernels.LaunchOutOptionalKernel(bias, o));
        Assert.Equal([1], o);
        Assert.Equal([5f], bias);

        bias = [1f];
        Run(forceManaged, () => OptionalArrayKernels.LaunchInOutOptionalKernel(bias, o));
        Assert.Equal([1], o);
        Assert.Equal([2f], bias);
    }

    // The managed execution path exists only on the CPU dialects
    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }
}
