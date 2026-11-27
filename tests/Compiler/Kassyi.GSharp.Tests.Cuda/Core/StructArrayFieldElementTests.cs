using System;
using System.Numerics;
using Kassyi.GSharp.Tests.Shared.Differential;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>A struct whose array field holds user structs: the element's shadow carries them to the device.</summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Runtime)]
public class StructArrayFieldElementTests
{
    // CPU passes the struct in its managed layout until L4
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ArrayFieldOfUserStructs_ByRef_IsCopiedBothWays()
    {
        var h = new ElemArrayHolder
        {
            Tag = 3,
            Items = [new PlainElem { X = 1, Y = 2 }, new PlainElem { X = 3, Y = 4 }]
        };
        var o = new int[2];

        StructArrayFieldElementKernels.LaunchElemArrayKernel(ref h, o);

        Assert.Equal([3, 4], o);
        Assert.Equal(42, h.Items[0].X);
        Assert.Equal(3, h.Items[1].X);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(1, 3)]
    [InlineData(2, 2)]
    public void LaunchConvertedElementsKernel_OverlappingHeldViews_ThrowsNotSupportedException(
        int secondStart,
        int secondLength)
    {
        CudaCapability.EnsureGpuAvailable();
        var elements = new ArrayViewElement[5];
        for (var i = 0; i < elements.Length; i++)
        {
            elements[i] = new ArrayViewElement { X = i + 1f, V = new Vector2(i, -i) };
        }

        int[] managed = [1, 2];
        var fields = new ArrayViewElementFields
        {
            Elements = elements.AsMemory(0, 4),
            ElementSegment = new ArraySegment<ArrayViewElement>(elements, secondStart, secondLength),
            Managed = managed.AsMemory(),
        };
        var r = new float[4];

        var exception = Assert.Throws<NotSupportedException>(
            () => ArrayViewFieldDifferentialKernels.LaunchConvertedElementsKernel(
                fields, r, gridDimX: 1, blockDimX: 4));

        Assert.Contains("converted between the host and device layouts", exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("not aligned", exception.Message, StringComparison.Ordinal);
    }
}
