using System;
using System.Collections.Immutable;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class OptionalArrayTests : TranspilerTestBase
{
    [Scenario(TestScenario.HappyPath)]
    [Fact]
    public void Test_IsNullBiasKernel_WithNonNullAndNull_ShouldExecuteCorrectly()
    {
        float[] x = [1f, 2f];
        OptionalArrayKernels.LaunchIsNullBiasKernel(x, [10f, 20f], blockDimX: 2);
        Assert.Equal([11f, 22f], x);

        x = [1f, 2f];
        OptionalArrayKernels.LaunchIsNullBiasKernel(x, null, blockDimX: 2);
        Assert.Equal([1f, 2f], x);
    }

    [Scenario(TestScenario.Boundary)]
    [Fact]
    public void Test_IsNullBiasKernel_WithEmptyBias_ShouldTreatAsNull()
    {
        float[] x = [1f, 2f];
        OptionalArrayKernels.LaunchIsNullBiasKernel(x, [], blockDimX: 2);
        Assert.Equal([1f, 2f], x);
    }

    [Scenario(TestScenario.HappyPath)]
    [Fact]
    public void Test_EqualsNullBiasKernel_WithNullAndNonNull_ShouldExecuteCorrectly()
    {
        float[] x = [1f, 2f];
        OptionalArrayKernels.LaunchEqualsNullBiasKernel(x, null, blockDimX: 2);
        Assert.Equal([-1f, -1f], x);

        x = [1f, 2f];
        OptionalArrayKernels.LaunchEqualsNullBiasKernel(x, [10f, 20f], blockDimX: 2);
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
}
