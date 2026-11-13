#nullable enable
using System;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.MarshalingDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for arguments that overlap one memory: the launchers give each group of overlapping arguments one
/// device region (CUDA) or one copy of its [In] members (CPU), so a write through one parameter is seen through the
/// others, as in C#.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class MarshalingOverlapDifferentialTests
{
    private static int[] Backing(int length)
    {
        var values = new int[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = i + 1;
        }

        return values;
    }

    internal static readonly DifferentialCase s_spanAndReadOnlySpan = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.SpanAndReadOnlySpanKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact)
            .Out<int>("observed", 1, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            MarshalingOverlapDifferentialKernels.LaunchSpanAndReadOnlySpanKernel(
                backing, backing, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_refElementAndArray = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.RefElementAndArrayKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(3), OutputPolicy.Exact)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            MarshalingOverlapDifferentialKernels.LaunchRefElementAndArrayKernel(
                ref backing[0], backing, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    // The group holds a writable member, so it is one holder that is copied back whole, the [In] part included
    internal static readonly DifferentialCase s_inSpanAndSpan = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.InSpanAndSpanKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(3), OutputPolicy.Exact)
            .Out<int>("observed", 1, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            MarshalingOverlapDifferentialKernels.LaunchInSpanAndSpanKernel(
                backing.AsSpan(0, 2), backing.AsSpan(1, 2), b.Get<int>("observed"), gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
    };

    // Every member is [In]: the kernel sees its own write through the other member, and nothing is copied back
    internal static readonly DifferentialCase s_twoInSpans = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.TwoInSpansKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(3), OutputPolicy.Exact, readOnly: true)
            .Out<int>("observed", 1, OutputPolicy.Exact),
        Reference = static (b, s) =>
        {
            int[] shadow = (int[])b.Get<int>("backing").Clone();
            SequentialOracle.Run(s, ThreadOrder.Forward, () =>
                MarshalingOverlapDifferentialKernels.LaunchTwoInSpansKernel(
                    shadow.AsSpan(0, 2), shadow.AsSpan(1, 2), b.Get<int>("observed"), gridDimX: s.GridX,
                    blockDimX: s.BlockX));
        },
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            MarshalingOverlapDifferentialKernels.LaunchTwoInSpansKernel(
                backing.AsSpan(0, 2), backing.AsSpan(1, 2), b.Get<int>("observed"), gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_inSpanAndOutArray = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.InSpanAndOutArrayKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(3), OutputPolicy.Exact)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            MarshalingOverlapDifferentialKernels.LaunchInSpanAndOutArrayKernel(
                backing.AsSpan(0, 2), backing, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_arrayAndSlice = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.ArrayAndSliceKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            MarshalingOverlapDifferentialKernels.LaunchArrayAndSliceKernel(
                backing, backing.AsSpan(1, 2), b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    // a and c do not overlap, but b overlaps both, so the three are one group
    internal static readonly DifferentialCase s_threeChainedSlices = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.ThreeChainedSlicesKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(8), OutputPolicy.Exact)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            MarshalingOverlapDifferentialKernels.LaunchThreeChainedSlicesKernel(
                backing.AsSpan(0, 3), backing.AsSpan(2, 3), backing.AsSpan(4, 3), b.Get<int>("observed"),
                gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_memoryAndSegment = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.MemoryAndSegmentKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(5), OutputPolicy.Exact)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            MarshalingOverlapDifferentialKernels.LaunchMemoryAndSegmentKernel(
                backing.AsMemory(0, 3), new ArraySegment<int>(backing, 1, 3), b.Get<int>("observed"),
                gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    // A long view over ints 2 to 5 of the same array: different element types, aligned for both
    internal static readonly DifferentialCase s_intAndLongViews = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.IntAndLongViewsKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(8), OutputPolicy.Exact)
            .Out<long>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            MarshalingOverlapDifferentialKernels.LaunchIntAndLongViewsKernel(
                backing.AsSpan(0, 6), MemoryMarshal.Cast<int, long>(backing.AsSpan(2, 4)), b.Get<long>("observed"),
                gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_twoRefStructs = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.TwoRefStructsKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("point", [new DiffOverlapPoint { X = 1, Y = 2 }])
            .Out<int>("observed", 1, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            DiffOverlapPoint[] point = b.Get<DiffOverlapPoint>("point");
            MarshalingOverlapDifferentialKernels.LaunchTwoRefStructsKernel(
                ref point[0], ref point[0], b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    // Slices that touch but do not overlap form no group: each keeps its own copy, as before
    internal static readonly DifferentialCase s_adjacentSlices = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.AdjacentSlicesKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(4), OutputPolicy.Exact)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            MarshalingOverlapDifferentialKernels.LaunchAdjacentSlicesKernel(
                backing.AsSpan(0, 2), backing.AsSpan(2, 2), b.Get<int>("observed"), gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
    };

    // The issue's shape: an [In] array and an [In] element's field hold one array. Nothing is copied back, so the
    // reference runs the C# on a copy of the array that both holders share
    internal static readonly DifferentialCase s_inArrayAndInField = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.InArrayAndInFieldKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            int[] shared = [1, 2];
            return new DiffBuffers()
                .In("array", shared, OutputPolicy.Exact, readOnly: true)
                .InDeep("bags", [new DiffDeepBag { Data = shared, Tag = 3 }], CloneDeepBag, CompareDeepBag)
                .Out<int>("observed", 1, OutputPolicy.Exact);
        },
        Reference = static (b, _) =>
        {
            int[] shadow = (int[])b.Get<int>("array").Clone();
            shadow[0] = 7;
            b.Get<int>("observed")[0] = shadow[0];
        },
        Launch = static (b, s) =>
        {
            int[] array = b.Get<int>("array");
            DiffDeepBag[] bags = b.Get<DiffDeepBag>("bags");
            bags[0].Data = array;
            MarshalingOverlapDifferentialKernels.LaunchInArrayAndInFieldKernel(
                array, bags, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    // Two fields of one element of an [In] container hold one array
    internal static readonly DifferentialCase s_twoInFields = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.TwoInFieldsKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .Out<int>("observed", 1, OutputPolicy.Exact),
        Reference = static (b, _) => b.Get<int>("observed")[0] = 9,
        Launch = static (b, s) =>
        {
            int[] shared = [4, 5];
            DiffArrayPair[] pairs = [new DiffArrayPair { First = shared, Second = shared }];
            MarshalingOverlapDifferentialKernels.LaunchTwoInFieldsKernel(
                pairs, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SpanOverlap_SpanAndReadOnlySpanOverOneArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_spanAndReadOnlySpan);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void RefOverlap_RefToAnElementAndItsArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_refElementAndArray);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SpanOverlap_InSpanAndWritableSpan_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_inSpanAndSpan);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SpanOverlap_TwoInSpans_SeeEachOthersWritesOnEveryPath() =>
        DifferentialRunner.Run(s_twoInSpans);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SpanOverlap_InSpanOverAnOutArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_inSpanAndOutArray);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SpanOverlap_ArrayAndASliceOfIt_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_arrayAndSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SpanOverlap_ThreeChainedSlices_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_threeChainedSlices);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SpanOverlap_MemoryAndArraySegment_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_memoryAndSegment);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SpanOverlap_IntAndLongViewsOfOneArray_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_intAndLongViews);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void RefOverlap_OneStructPassedToTwoRefParameters_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_twoRefStructs);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void SpanOverlap_AdjacentSlices_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_adjacentSlices);
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void InHolders_InArrayAndAnInElementFieldShareOneArray_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_inArrayAndInField);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void InHolders_TwoFieldsOfAnInElementShareOneArray_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_twoInFields);
}
