#nullable enable
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Interop;
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

    // The issue's read-only container form: a ReadOnlySpan of structs whose field holds the [In] array
    internal static readonly DifferentialCase s_inArrayAndReadOnlySpanField = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.InArrayAndReadOnlySpanFieldKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("array", [1, 2], OutputPolicy.Exact, readOnly: true)
            .Out<int>("observed", 1, OutputPolicy.Exact),
        Reference = static (b, _) => b.Get<int>("observed")[0] = 7,
        Launch = static (b, s) =>
        {
            int[] array = b.Get<int>("array");
            DiffDeepBag[] bags = [new DiffDeepBag { Data = array, Tag = 3 }];
            MarshalingOverlapDifferentialKernels.LaunchInArrayAndReadOnlySpanFieldKernel(
                array, bags, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    // Both views are [In]: the kernel sees its writes through the other view, and nothing reaches the caller
    internal static readonly DifferentialCase s_inIntAndLongViews = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.InIntAndLongViewsKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(8), OutputPolicy.Exact, readOnly: true)
            .Out<long>("observed", 2, OutputPolicy.Exact),
        Reference = static (b, s) =>
        {
            int[] shadow = (int[])b.Get<int>("backing").Clone();
            SequentialOracle.Run(s, ThreadOrder.Forward, () =>
                MarshalingOverlapDifferentialKernels.LaunchInIntAndLongViewsKernel(
                    shadow.AsSpan(0, 6), MemoryMarshal.Cast<int, long>(shadow.AsSpan(2, 4)), b.Get<long>("observed"),
                    gridDimX: s.GridX, blockDimX: s.BlockX));
        },
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            MarshalingOverlapDifferentialKernels.LaunchInIntAndLongViewsKernel(
                backing.AsSpan(0, 6), MemoryMarshal.Cast<int, long>(backing.AsSpan(2, 4)), b.Get<long>("observed"),
                gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    // A Memory<long> over ints 2 to 5 of an [In] int[]: the CPU copy is typed by the array, and the memory views its
    // bytes through a MemoryManager
    internal static readonly DifferentialCase s_inArrayAndLongMemory = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.InArrayAndLongMemoryKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(8), OutputPolicy.Exact, readOnly: true)
            .Out<long>("observed", 2, OutputPolicy.Exact),
        Reference = static (b, s) =>
        {
            int[] shadow = (int[])b.Get<int>("backing").Clone();
            SequentialOracle.Run(s, ThreadOrder.Forward, () =>
                MarshalingOverlapDifferentialKernels.LaunchInArrayAndLongMemoryKernel(
                    shadow, LongsOverInts(shadow), b.Get<long>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX));
        },
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            MarshalingOverlapDifferentialKernels.LaunchInArrayAndLongMemoryKernel(
                backing, LongsOverInts(backing), b.Get<long>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    // A uint segment over the same int[] (the CLR lets an int[] be a uint[]): one copy, reinterpreted
    internal static readonly DifferentialCase s_inArrayAndUintSegment = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.InArrayAndUintSegmentKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Backing(5), OutputPolicy.Exact, readOnly: true)
            .Out<long>("observed", 2, OutputPolicy.Exact),
        Reference = static (b, s) =>
        {
            int[] shadow = (int[])b.Get<int>("backing").Clone();
            SequentialOracle.Run(s, ThreadOrder.Forward, () =>
                MarshalingOverlapDifferentialKernels.LaunchInArrayAndUintSegmentKernel(
                    shadow, new ArraySegment<uint>((uint[])(object)shadow, 1, 3), b.Get<long>("observed"),
                    gridDimX: s.GridX, blockDimX: s.BlockX));
        },
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            MarshalingOverlapDifferentialKernels.LaunchInArrayAndUintSegmentKernel(
                backing, new ArraySegment<uint>((uint[])(object)backing, 1, 3), b.Get<long>("observed"),
                gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    // A ref to a struct that holds an array, and a span of the array of such structs that holds it
    internal static readonly DifferentialCase s_refAndSpanOfDeepBags = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.RefAndSpanOfDeepBagsKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .InDeep(
                "bags",
                [
                    new DiffDeepBag { Data = [1], Tag = 10 },
                    new DiffDeepBag { Data = [2], Tag = 20 },
                    new DiffDeepBag { Data = [3], Tag = 30 }
                ],
                CloneDeepBag,
                CompareDeepBag)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            DiffDeepBag[] bags = b.Get<DiffDeepBag>("bags");
            MarshalingOverlapDifferentialKernels.LaunchRefAndSpanOfDeepBagsKernel(
                ref bags[1], bags.AsSpan(0, 2), b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    // ints 2 to 5 of the array as two longs
    private static Memory<long> LongsOverInts(int[] ints) =>
        new HostAliasMemoryManager<long>(ints, 2 * sizeof(int), 2).Memory;

    internal static readonly DifferentialCase s_twoArraysSliceAndStruct = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.TwoArraysSliceAndStructKernel)))
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
            var bag = new DiffDeepBag { Data = [9], Tag = 100 };
            MarshalingOverlapDifferentialKernels.LaunchTwoArraysSliceAndStructKernel(
                backing, backing, backing.AsSpan(1), bag, b.Get<int>("observed"), gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
    };

    private static DiffVec2Padded[] Padded(int length)
    {
        var values = new DiffVec2Padded[length];
        for (var i = 0; i < length; i++)
        {
            values[i] = new DiffVec2Padded { A = i + 1, V = new Vector2(10 * (i + 1), 0.5f) };
        }

        return values;
    }

    internal static readonly DifferentialCase s_convertedSlices = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.ConvertedSlicesKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Padded(4))
            .Out<float>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            DiffVec2Padded[] backing = b.Get<DiffVec2Padded>("backing");
            MarshalingOverlapDifferentialKernels.LaunchConvertedSlicesKernel(
                backing.AsSpan(0, 3), backing.AsSpan(1, 3), b.Get<float>("observed"), gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_inConvertedSlices = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.InConvertedSlicesKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", Padded(3), policy: null, comparer: null, readOnly: true)
            .Out<float>("observed", 1, OutputPolicy.Exact),
        Reference = static (b, _) => b.Get<float>("observed")[0] = 7f,
        Launch = static (b, s) =>
        {
            DiffVec2Padded[] backing = b.Get<DiffVec2Padded>("backing");
            MarshalingOverlapDifferentialKernels.LaunchInConvertedSlicesKernel(
                backing.AsSpan(0, 2), backing.AsSpan(1, 2), b.Get<float>("observed"), gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_twoConvertedRefs = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.TwoConvertedRefsKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("value", Padded(1))
            .Out<float>("observed", 1, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            DiffVec2Padded[] value = b.Get<DiffVec2Padded>("value");
            MarshalingOverlapDifferentialKernels.LaunchTwoConvertedRefsKernel(
                ref value[0], ref value[0], b.Get<float>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_deepBagSlices = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.DeepBagSlicesKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .InDeep(
                "bags",
                [
                    new DiffDeepBag { Data = [1], Tag = 10 },
                    new DiffDeepBag { Data = [2], Tag = 20 },
                    new DiffDeepBag { Data = [3], Tag = 30 }
                ],
                CloneDeepBag,
                CompareDeepBag)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            DiffDeepBag[] bags = b.Get<DiffDeepBag>("bags");
            MarshalingOverlapDifferentialKernels.LaunchDeepBagSlicesKernel(
                bags.AsSpan(0, 2), bags.AsSpan(1, 2), b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_shiftedPoints = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.ShiftedPointsKernel)))
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
            MarshalingOverlapDifferentialKernels.LaunchShiftedPointsKernel(
                ref Unsafe.As<int, DiffOverlapPoint>(ref backing[0]),
                ref Unsafe.As<int, DiffOverlapPoint>(ref backing[1]),
                b.Get<int>("observed"),
                gridDimX: s.GridX,
                blockDimX: s.BlockX);
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedOverlap_SlicesOfStructsOfAnotherDeviceLayout_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_convertedSlices);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedOverlap_InSlicesOfStructsOfAnotherDeviceLayout_SeeEachOthersWritesOnEveryPath() =>
        DifferentialRunner.Run(s_inConvertedSlices);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedOverlap_OneConvertedStructToTwoRefParameters_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_twoConvertedRefs);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedOverlap_SlicesOfStructsThatHoldArrays_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_deepBagSlices);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void RefOverlap_TwoStructsOfFourByteAlignmentFourBytesApart_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_shiftedPoints);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void InHolders_InArrayAndAReadOnlySpanElementField_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_inArrayAndReadOnlySpanField);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SpanOverlap_InIntAndLongViewsOfOneArray_SeeEachOthersWritesOnEveryPath() =>
        DifferentialRunner.Run(s_inIntAndLongViews);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void InHolders_InArrayAndALongMemoryOverIt_SeeEachOthersWritesOnEveryPath() =>
        DifferentialRunner.Run(s_inArrayAndLongMemory);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void InHolders_InArrayAndAUintSegmentOfTheSameArray_SeeEachOthersWritesOnEveryPath() =>
        DifferentialRunner.Run(s_inArrayAndUintSegment);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedOverlap_RefAndSpanOfStructsThatHoldArrays_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_refAndSpanOfDeepBags);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SpanOverlap_TwoArraysOfOneArraySliceAndStruct_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_twoArraysSliceAndStruct);

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

    // One variable to an in and a ref parameter: the write through the ref shows through the in, as in C#
    internal static readonly DifferentialCase s_inAndRefOneVariable = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.InAndRefOneVariableKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .Out<int>("observed", 1, OutputPolicy.Exact)
            .Out<int>("final", 1, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 15;
            b.Get<int>("final")[0] = 15;
        },
        Launch = static (b, s) =>
        {
            var variable = 5;
            MarshalingOverlapDifferentialKernels.LaunchInAndRefOneVariableKernel(
                in variable, ref variable, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
            b.Get<int>("final")[0] = variable;
        },
    };

    // Two variables: the in keeps the caller's value, and nothing else changes
    internal static readonly DifferentialCase s_inAndRefTwoVariables = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.InAndRefOneVariableKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .Out<int>("observed", 1, OutputPolicy.Exact)
            .Out<int>("final", 1, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 5;
            b.Get<int>("final")[0] = 15;
        },
        Launch = static (b, s) =>
        {
            var first = 5;
            var second = 5;
            MarshalingOverlapDifferentialKernels.LaunchInAndRefOneVariableKernel(
                in first, ref second, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
            b.Get<int>("final")[0] = second;
        },
    };

    // One variable to an in and an out parameter: the in reads what the kernel assigned to the out
    internal static readonly DifferentialCase s_inAndOutOneVariable = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.InAndOutOneVariableKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .Out<int>("observed", 1, OutputPolicy.Exact)
            .Out<int>("final", 1, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<int>("observed")[0] = 7;
            b.Get<int>("final")[0] = 7;
        },
        Launch = static (b, s) =>
        {
            var variable = 5;
            MarshalingOverlapDifferentialKernels.LaunchInAndOutOneVariableKernel(
                in variable, out variable, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
            b.Get<int>("final")[0] = variable;
        },
    };

    // A struct whose host and device layouts differ: the group is converted as a whole, and the in sees the write
    internal static readonly DifferentialCase s_inAndRefConverted = new(
        KernelName(nameof(MarshalingOverlapDifferentialKernels.InAndRefConvertedKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .Out<float>("observed", 1, OutputPolicy.Exact)
            .Out<float>("final", 1, OutputPolicy.Exact),
        Reference = static (b, _) =>
        {
            b.Get<float>("observed")[0] = 11f;
            b.Get<float>("final")[0] = 11f;
        },
        Launch = static (b, s) =>
        {
            var variable = new DiffVec2Padded { A = 1f };
            MarshalingOverlapDifferentialKernels.LaunchInAndRefConvertedKernel(
                in variable, ref variable, b.Get<float>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
            b.Get<float>("final")[0] = variable.A;
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void InOverlap_OneVariablePassedToAnInAndARefParameter_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_inAndRefOneVariable);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void InOverlap_TwoVariablesToAnInAndARefParameter_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_inAndRefTwoVariables);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void InOverlap_OneVariablePassedToAnInAndAnOutParameter_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_inAndOutOneVariable);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void InOverlap_OneConvertedStructPassedToAnInAndARefParameter_MatchesTheReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_inAndRefConverted);
}
