#nullable enable
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// A kernel write to an <c>[In]</c> array, or through a read-only container to the arrays its elements hold, never
/// reaches the caller: the CUDA launcher copies such an argument to the device and never back, and the CPU paths (the
/// native launcher and the managed fallback) give the kernel a copy so that they produce the same result (#798).
/// </summary>
/// <remarks>
/// The expected values are the inputs, and <c>o</c> shows that the kernel read them before writing.
/// </remarks>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class InArrayCopyTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void InIntArray_KernelWrites_DoNotReachTheCaller(bool forceManaged)
    {
        int[] input = [1, 2, 3, 4];
        var o = new int[4];

        Run(forceManaged, () => InArrayCopyKernels.LaunchInIntArrayKernel(input, o, blockDimX: 4));

        Assert.Equal([1, 2, 3, 4], o);
        Assert.Equal([1, 2, 3, 4], input);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void InIntSpan_KernelWrites_DoNotReachTheCaller(bool forceManaged)
    {
        int[] input = [1, 2, 3, 4];
        var o = new int[4];

        Run(forceManaged, () => InArrayCopyKernels.LaunchInIntSpanKernel(input.AsSpan(), o, blockDimX: 4));

        Assert.Equal([1, 2, 3, 4], o);
        Assert.Equal([1, 2, 3, 4], input);
    }

    // Neither the scalar field nor the array field of a converted element changes
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void InConvertedArray_KernelWrites_DoNotReachTheCaller(bool forceManaged)
    {
        var bags = new[]
        {
            new ConvertBag { N = 1, Data = [3, 0] },
            new ConvertBag { N = 2, Data = [7, 0] }
        };
        var o = new int[2];

        Run(forceManaged, () => InArrayCopyKernels.LaunchInBagArrayKernel(bags, o, blockDimX: 2));

        Assert.Equal([4, 9], o);
        Assert.Equal((1, 2), (bags[0].N, bags[1].N));
        Assert.Equal([3, 0], bags[0].Data);
        Assert.Equal([7, 0], bags[1].Data);
    }

    // An optional [In] argument that is null stays null and arrives with length 0, also on the managed fallback (#806)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void InNullableConvertedArray_Null_IsPassedWithLengthZero(bool forceManaged)
    {
        var o = new[] { -1 };

        Run(forceManaged, () => InArrayCopyKernels.LaunchInNullableBagArrayKernel(null, o));

        Assert.Equal(0, o[0]);
    }

    // A read-only container keeps its elements; the arrays they hold are the one thing a kernel can write through it
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadOnlySpanOfConvertedElements_ArrayFieldWrites_DoNotReachTheCaller(bool forceManaged)
    {
        var bags = new[]
        {
            new ConvertBag { N = 1, Data = [3, 0] },
            new ConvertBag { N = 2, Data = [7, 0] }
        };
        var o = new int[2];

        Run(forceManaged, () => InArrayCopyKernels.LaunchReadOnlySpanBagKernel(bags, o, blockDimX: 2));

        Assert.Equal([3, 7], o);
        Assert.Equal([3, 0], bags[0].Data);
        Assert.Equal([7, 0], bags[1].Data);
    }

    // The elements of an array field and the arrays they hold are copied as well
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void InArrayOfArrayFieldsWithArrays_KernelWrites_DoNotReachTheCaller(bool forceManaged)
    {
        var holders = new[]
        {
            new LayoutReproElemHolder { N = 1, Elems = [new LayoutReproInnerBag { Tag = 2, Items = [3] }] }
        };
        var o = new int[1];

        Run(forceManaged, () => InArrayCopyKernels.LaunchInElemHolderArrayKernel(holders, o));

        Assert.Equal(3, o[0]);
        Assert.Equal(2L, holders[0].Elems[0].Tag);
        Assert.Equal([3], holders[0].Elems[0].Items);
    }

    // The arrays held by the elements of an inline array inside a nested struct of the element are copied as well
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void InArrayOfNestedInlineArrays_KernelWrites_DoNotReachTheCaller(bool forceManaged)
    {
        var holder = new ConvertBagPairHolder { Tag = 1 };
        holder.Bags[0] = new ConvertBag { N = 2, Data = [4] };
        holder.Bags[1] = new ConvertBag { N = 3, Data = [5] };
        var holders = new[] { holder };
        var o = new int[1];

        Run(forceManaged, () => InArrayCopyKernels.LaunchInPairHolderArrayKernel(holders, o));

        Assert.Equal(6, o[0]);
        Assert.Equal(1, holders[0].Tag);
        Assert.Equal([4], holders[0].Bags[0].Data);
        Assert.Equal([5], holders[0].Bags[1].Data);
    }

    // The managed path exists only on the CPU dialects
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
