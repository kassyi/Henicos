using System;
using System.Collections.Immutable;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;
using Kassyi.GSharp.Runtime;

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
    public void IsNullBias_NonNullAndNullInputs_ComputesExpectedValues(bool forceManaged)
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
    public void IsNullOptional_EmptyAndNullBias_OnlyNullIsNull(bool forceManaged)
    {
        int[] o = [-1];
        Run(forceManaged, () => OptionalArrayKernels.LaunchIsNullOptionalKernel(o, []));
        Assert.Equal([0], o);

        Run(forceManaged, () => OptionalArrayKernels.LaunchIsNullOptionalKernel(o, null));
        Assert.Equal([1], o);
    }

    [Scenario(TestScenario.HappyPath)]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualsNullBias_NullAndNonNullInputs_ComputesExpectedValues(bool forceManaged)
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
    public void MemoryAdd_SpanInputs_ModifiesBackingArray()
    {
        float[] xArray = [1f, 2f];
        float[] yArray = [10f, 20f];
        OptionalArrayKernels.LaunchMemoryAddKernel(xArray.AsMemory(), yArray.AsMemory(), blockDimX: 2);
        Assert.Equal([11f, 22f], xArray);
    }

    [Scenario(TestScenario.Boundary)]
    [Fact]
    public void SegmentScale_WithOffsetAndCount_HonorsOffsetAndCount()
    {
        float[] backing = [0f, 1f, 2f, 3f];
        var segment = new ArraySegment<float>(backing, 1, 2);
        OptionalArrayKernels.LaunchSegmentScaleKernel(segment, blockDimX: 2);
        Assert.Equal([0f, 4f, 6f, 3f], backing);
    }

    [Scenario(TestScenario.HappyPath)]
    [Fact]
    public void ImmutableAdd_ImmutableArrayInput_LeavesImmutableArrayUnchanged()
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
    public void OptionalMemory_NullDefaultAndValues_ComputesExpectedValues()
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
    public void OptionalSegment_NullAndValues_ComputesExpectedValues()
    {
        float[] x = [1f, 2f];
        OptionalArrayKernels.LaunchOptionalSegmentKernel(x, null, blockDimX: 2);
        Assert.Equal([1f, 2f], x);

        x = [1f, 2f];
        OptionalArrayKernels.LaunchOptionalSegmentKernel(x, new ArraySegment<float>([10f, 20f]), blockDimX: 2);
        Assert.Equal([11f, 22f], x);
    }

    // C# does not check the annotation of a parameter: a null non-nullable array reaches the kernel, whose first read
    // throws
    [Scenario(TestScenario.SadPath)]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsNullBias_NullInputArray_ThrowsAtTheRead(bool forceManaged)
    {
        // Intentionally passing null where the parameter is not nullable, as C# lets a caller do
        Assert.Throws<NullReferenceException>(() =>
            Run(forceManaged, () => OptionalArrayKernels.LaunchIsNullBiasKernel(null!, [10f, 20f], blockDimX: 2)));
    }

    [Scenario(TestScenario.SadPath)]
    [Fact]
    public void MemoryAdd_EmptyInputArray_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            OptionalArrayKernels.LaunchMemoryAddKernel(Memory<float>.Empty, new float[] { 10f, 20f }, blockDimX: 2));
        Assert.Equal("x", ex.ParamName);
    }

    // The Length of a null optional array throws, as in C#, on every path
    [Scenario(TestScenario.SadPath)]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LengthOptional_NullBias_ThrowsNullReference(bool forceManaged)
    {
        var o = new[] { -1, -1 };
        Assert.Throws<NullReferenceException>(() =>
            Run(forceManaged, () => OptionalArrayKernels.LaunchLengthOptionalKernel(o, null)));
        Assert.Equal([-1, -1], o);
    }

    // An empty optional array is not null and has length 0
    [Scenario(TestScenario.Boundary)]
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LengthOptional_EmptyAndFilledBias_ReadsTheirLength(bool forceManaged)
    {
        var o = new[] { -1, -1 };
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
    public void WriteOptional_NullAndValueInputs_ProducesAgreedOutputs(bool forceManaged)
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

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}
