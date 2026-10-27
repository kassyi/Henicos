using System;
using System.Numerics;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// The containers of an array of structs whose host and device layouts differ: the launcher converts the elements
/// through a shadow array and copies them back as the container allows (ConvertPair differs on CUDA only, ConvertBag on
/// both dialects).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
public class StructConvertTransferTests
{
    private static ConvertPair[] CreatePairs() =>
    [
        new ConvertPair { A = 1f, V = new Vector2(2f, 3f) },
        new ConvertPair { A = 4f, V = new Vector2(5f, 6f) }
    ];

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Span_ConvertedElements_AreCopiedBack()
    {
        var items = CreatePairs();

        StructConvertTransferKernels.LaunchSpanKernel(items.AsSpan(), blockDimX: 2);

        Assert.Equal(4f, items[0].A);
        Assert.Equal(10f, items[1].A);
        Assert.Equal(new Vector2(5f, 6f), items[1].V);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ReadOnlySpan_ConvertedElements_AreRead()
    {
        var items = CreatePairs();
        var o = new float[2];

        StructConvertTransferKernels.LaunchReadOnlySpanKernel(items, o, blockDimX: 2);

        Assert.Equal([4f, 10f], o);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void InArray_ConvertedElements_AreNotCopiedBack()
    {
        var items = CreatePairs();
        var o = new float[2];

        StructConvertTransferKernels.LaunchInArrayKernel(items, o, blockDimX: 2);

        Assert.Equal([3f, 6f], o);
#if !GSHARP_CPU
        // The CPU kernel works on the pinned host array itself, so [In] cannot keep its writes
        Assert.Equal(1f, items[0].A);
        Assert.Equal(4f, items[1].A);
#endif
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void EmptyArray_IsPassedWithLengthZero()
    {
        var o = new[] { -1 };

        StructConvertTransferKernels.LaunchEmptyArrayKernel([], o);

        Assert.Equal(0, o[0]);
    }

    // An empty array is accepted by a nullable parameter and arrives with length 0
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NullArray_IsPassedWithLengthZero()
    {
        var o = new[] { -1 };

        StructConvertTransferKernels.LaunchEmptyArrayKernel(null, o);

        Assert.Equal(0, o[0]);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void InOutArrayWithArrayFields_CopiesBackFieldsAndArrays()
    {
        var bags = new[]
        {
            new ConvertBag { N = 1, Data = [3, 0] },
            new ConvertBag { N = 2, Data = [7, 0] }
        };

        StructConvertTransferKernels.LaunchInOutBagKernel(bags, blockDimX: 2);

        Assert.Equal(2, bags[0].N);
        Assert.Equal([3, 6], bags[0].Data);
        Assert.Equal(3, bags[1].N);
        Assert.Equal([7, 14], bags[1].Data);
    }

    // [Out] skips copying the elements in, but the array fields of the elements must still reach the device
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void OutArrayWithArrayFields_CopiesBackFieldsAndArrays()
    {
        var bags = new[]
        {
            new ConvertBag { N = 0, Data = [0, 1] },
            new ConvertBag { N = 0, Data = [0, 2] }
        };

        StructConvertTransferKernels.LaunchOutBagKernel(bags, blockDimX: 2);

        Assert.Equal(5, bags[0].N);
        Assert.Equal(9, bags[0].Data[0]);
        Assert.Equal(6, bags[1].N);
        Assert.Equal(10, bags[1].Data[0]);
    }

    // The shadow array comes from ArrayPool, so the second launch gets the bytes of the first one back; a null array
    // field must not leave the freed device pointer of the first launch in it
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ArrayWithArrayFields_SecondLaunchWithNullFields_SeesNoStaleArray()
    {
        var o = new int[3];
        StructConvertTransferKernels.LaunchBagReadKernel(
            [new ConvertBag { N = 3, Data = [10, 20, 30] }, new ConvertBag { N = 4, Data = [1] }],
            o);
        Assert.Equal([3, 3, 4], o);

        StructConvertTransferKernels.LaunchBagReadKernel([new ConvertBag { N = 5 }, new ConvertBag { N = 6 }], o);

        Assert.Equal([5, 0, 6], o);
    }

    // The same through an inline array of elements with array fields inside the converted element
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void InlineArrayOfArrayFields_SecondLaunchWithNullFields_SeesNoStaleArray()
    {
        var o = new int[2];
        var first = new ConvertBagPairHolder { Tag = 1 };
        first.Bags[1] = new ConvertBag { N = 2, Data = [1, 2, 3] };
        StructConvertTransferKernels.LaunchPairHolderReadKernel([first, first], o);
        Assert.Equal([1, 3], o);

        var second = new ConvertBagPairHolder { Tag = 2 };
        StructConvertTransferKernels.LaunchPairHolderReadKernel([second, second], o);

        Assert.Equal([2, 0], o);
    }

    // The same for an [Out] array, whose elements are still marshaled because they have array fields
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void OutArrayWithArrayFields_SecondLaunchWithNullFields_SeesNoStaleArray()
    {
        var o = new int[2];
        StructConvertTransferKernels.LaunchOutBagLengthKernel(
            [new ConvertBag { Data = [1, 2] }, new ConvertBag { Data = [3] }],
            o,
            blockDimX: 2);
        Assert.Equal([2, 1], o);

        var bags = new[] { new ConvertBag(), new ConvertBag() };
        StructConvertTransferKernels.LaunchOutBagLengthKernel(bags, o, blockDimX: 2);

        Assert.Equal([0, 0], o);
        Assert.Equal([7, 7], bags.Select(static b => b.N));
    }
}
