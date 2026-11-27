using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Marshaling;

public struct AliasPlanArrayBag
{
    public int[] Data;
}

public struct AliasPlanMemoryBag
{
    public Memory<int> Data;
}

public struct AliasPlanTwoViews
{
    public Memory<int> A;
    public Memory<int> B;
}

public struct AliasPlanOuterTwoBags
{
    public AliasPlanMemoryBag First;
    public AliasPlanMemoryBag Second;
}

[InlineArray(2)]
public struct AliasPlanInlineTwoBags
{
    private AliasPlanMemoryBag _element;
}

public struct AliasPlanInlineHolder
{
    public AliasPlanInlineTwoBags Items;
}

public struct AliasPlanNullableMemoryBag
{
    public Memory<int>? Data;
}

public struct AliasPlanTwoInnerViewFields
{
    public Memory<AliasPlanMemoryBag> First;
    public Memory<AliasPlanMemoryBag> Second;
}

public struct AliasPlanTwoNestedArrayViews
{
    public Memory<int[]> First;
    public Memory<int[]> Second;
}

public struct AliasPlanMixedInnerViews
{
    public Memory<AliasPlanMemoryBag> First;
    public ReadOnlyMemory<AliasPlanMemoryBag> Second;
}

public struct AliasPlanNullableNestedMemoryBag
{
    public Memory<Memory<int>>? Views;
}

public struct AliasPlanNullableNestedArrayBag
{
    public Memory<int[]>? Arrays;
}

public struct AliasPlanNullableNestedOptionalMemoryBag
{
    public Memory<Memory<int>?>? OptionalViews;
}

public struct AliasPlanDeepLeafBag
{
    public Memory<int> Data;
}

public struct AliasPlanTwoDeepNestedViewFields
{
    public Memory<Memory<AliasPlanDeepLeafBag>> First;
    public Memory<Memory<AliasPlanDeepLeafBag>> Second;
}

public struct AliasPlanThreeLevelOptionalViewBag
{
    public Memory<Memory<Memory<long>?>> OptionalViews;
}

public struct AliasPlanNullableDeepNestedViewBag
{
    public Memory<Memory<AliasPlanDeepLeafBag>>? Views;
}

public struct AliasPlanNullableThreeLevelOptionalViewBag
{
    public Memory<Memory<Memory<long>?>>? OptionalViews;
}

public static partial class LaunchAliasPlanTestKernels
{
    [GpuKernel]
    public static void SingleArrayBagAlone(AliasPlanArrayBag h)
    {
    }

    [GpuKernel]
    public static void TwoArrayBagsAlone(AliasPlanArrayBag a, AliasPlanArrayBag b)
    {
    }

    [GpuKernel]
    public static void SingleViewBagAlone(AliasPlanMemoryBag h)
    {
    }

    [GpuKernel]
    public static void SingleViewBagWithArrayCandidate(AliasPlanMemoryBag h, int[] observed)
    {
    }

    [GpuKernel]
    public static void StructWithTwoViewsAlone(AliasPlanTwoViews h)
    {
    }

    [GpuKernel]
    public static void NestedTwoViewsAlone(AliasPlanOuterTwoBags h)
    {
    }

    [GpuKernel]
    public static void InlineArrayOfTwoViewsAlone(AliasPlanInlineHolder h)
    {
    }

    [GpuKernel]
    public static void TwoSingleViewBagsAlone(AliasPlanMemoryBag a, AliasPlanMemoryBag b)
    {
    }

    [GpuKernel]
    public static void ArrayBagAndViewBagAlone(AliasPlanArrayBag a, AliasPlanMemoryBag b)
    {
    }

    [GpuKernel]
    public static void ArrayOfViewBagsAndWholeArray(AliasPlanMemoryBag[] bags, int[] whole)
    {
    }

    [GpuKernel]
    public static void OutViewBagAndSlice(out AliasPlanMemoryBag bag, Span<int> slice)
    {
        bag = default;
    }

    [GpuKernel]
    public static void NullableViewFieldAndWholeArray(AliasPlanNullableMemoryBag bag, int[] whole)
    {
    }

    [GpuKernel]
    public static void InArrayOfViewBagsAndInSlice([In] AliasPlanMemoryBag[] bags, [In] Span<int> slice)
    {
    }

    [GpuKernel]
    public static void TwoInnerViewFieldsAlone(AliasPlanTwoInnerViewFields h)
    {
    }

    [GpuKernel]
    public static void TwoNestedArrayViewsAlone(AliasPlanTwoNestedArrayViews h)
    {
    }

    [GpuKernel]
    public static void MixedInnerViewsAlone(AliasPlanMixedInnerViews h)
    {
    }

    [GpuKernel]
    public static void NullableNestedMemoryAlone(AliasPlanNullableNestedMemoryBag h)
    {
    }

    [GpuKernel]
    public static void NullableNestedArrayAlone(AliasPlanNullableNestedArrayBag h)
    {
    }

    [GpuKernel]
    public static void NullableNestedOptionalMemoryAlone(AliasPlanNullableNestedOptionalMemoryBag h)
    {
    }

    [GpuKernel]
    public static void TwoDeepNestedViewFieldsAlone(AliasPlanTwoDeepNestedViewFields h)
    {
    }

    [GpuKernel]
    public static void ThreeLevelOptionalViewAlone(AliasPlanThreeLevelOptionalViewBag h)
    {
    }

    [GpuKernel]
    public static void NullableDeepNestedViewAlone(AliasPlanNullableDeepNestedViewBag h)
    {
    }

    [GpuKernel]
    public static void NullableThreeLevelOptionalViewAlone(AliasPlanNullableThreeLevelOptionalViewBag h)
    {
    }
}
