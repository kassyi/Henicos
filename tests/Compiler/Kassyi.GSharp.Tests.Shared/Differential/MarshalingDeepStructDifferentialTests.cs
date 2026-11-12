#nullable enable
using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.MarshalingDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for deep struct marshaling (nested deep structs across value/in/ref,
/// 6 container shapes, and C# 12 primary constructors).
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class MarshalingDeepStructDifferentialTests
{
    internal static readonly DifferentialCase s_deepStructAllArgModes = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.DeepStructAllArgModesKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            DiffNestedDeep valBag = new()
            {
                Indices = [2, 1, 0],
                Inner = new DiffInnerBag { Weights = [1.5f, 2.5f, 3.5f], Bias = 10 },
                Scale = 2,
            };
            DiffNestedDeep inBag = new()
            {
                Indices = [0, 2, 1],
                Inner = new DiffInnerBag { Weights = [4.0f, 5.0f, 6.0f], Bias = 20 },
                Scale = 3,
            };
            DiffNestedDeep refBag = new()
            {
                Indices = [5, 6],
                Inner = new DiffInnerBag { Weights = [10.0f, 20.0f], Bias = 7 },
                Scale = 4,
            };
            return new DiffBuffers()
                .InDeepScalar("byVal", valBag, CloneNestedDeep, CompareNestedDeep)
                .InDeepScalar("byIn", inBag, CloneNestedDeep, CompareNestedDeep, readOnly: true)
                .RefDeepScalar("byRef", refBag, CloneNestedDeep, CompareNestedDeep)
                .Out<float>("outSums", 3, OutputPolicy.Bitwise);
        },
        Oracle = static (b, _) =>
        {
            DiffNestedDeep byVal = b.GetScalar<DiffNestedDeep>("byVal");
            DiffNestedDeep byIn = b.GetScalar<DiffNestedDeep>("byIn");
            ref DiffNestedDeep byRef = ref b.GetScalarRef<DiffNestedDeep>("byRef");
            float[] outSums = b.Get<float>("outSums");
            MarshalingAliasingDifferentialKernels.DeepStructAllArgModesKernel(byVal, in byIn, ref byRef, outSums);
        },
        Launch = static (b, s) =>
        {
            DiffNestedDeep byVal = b.GetScalar<DiffNestedDeep>("byVal");
            DiffNestedDeep byIn = b.GetScalar<DiffNestedDeep>("byIn");
            ref DiffNestedDeep byRef = ref b.GetScalarRef<DiffNestedDeep>("byRef");
            float[] outSums = b.Get<float>("outSums");
            MarshalingAliasingDifferentialKernels.LaunchDeepStructAllArgModesKernel(
                byVal, byIn, ref byRef, outSums, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_deepStructSixContainers = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.DeepStructSixContainersKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            DiffDeepBag[] arrBags = [new DiffDeepBag { Data = [10, 11], Tag = 1 }];
            DiffDeepBag[] spanBacking = [new DiffDeepBag { Data = [20, 21], Tag = 2 }];
            DiffDeepBag[] roSpanBacking = [new DiffDeepBag { Data = [30, 31], Tag = 3 }];
            // ArraySegment with non-zero Offset = 1, Count = 2!
            DiffDeepBag[] segBacking =
            [
                new DiffDeepBag { Data = [999, 999], Tag = 99 },
                new DiffDeepBag { Data = [40, 41], Tag = 4 },
                new DiffDeepBag { Data = [42, 43], Tag = 5 },
                new DiffDeepBag { Data = [888, 888], Tag = 88 },
            ];
            DiffDeepBag[] memBacking = [new DiffDeepBag { Data = [50, 51], Tag = 6 }];
            DiffDeepBag[] immBacking =
            [
                new DiffDeepBag { Data = [60, 61], Tag = 7 },
                new DiffDeepBag { Data = [62, 63], Tag = 8 },
            ];

            return new DiffBuffers()
                .InDeep("arrBags", arrBags, CloneDeepBag, CompareDeepBag)
                .InDeep("spanBacking", spanBacking, CloneDeepBag, CompareDeepBag)
                .InDeep("roSpanBacking", roSpanBacking, CloneDeepBag, CompareDeepBag, readOnly: true)
                .InDeep("segBacking", segBacking, CloneDeepBag, CompareDeepBag)
                .InDeep("memBacking", memBacking, CloneDeepBag, CompareDeepBag)
                .InDeep("immBacking", immBacking, CloneDeepBag, CompareDeepBag, readOnly: true)
                .Out<int>("outTotals", 6, OutputPolicy.Exact);
        },
        Oracle = static (b, _) =>
        {
            DiffDeepBag[] arrBags = b.Get<DiffDeepBag>("arrBags");
            Span<DiffDeepBag> spanBags = b.Get<DiffDeepBag>("spanBacking").AsSpan();
            ReadOnlySpan<DiffDeepBag> roSpanBags = b.Get<DiffDeepBag>("roSpanBacking");
            ArraySegment<DiffDeepBag> segBags = new(b.Get<DiffDeepBag>("segBacking"), offset: 1, count: 2);
            Memory<DiffDeepBag> memBags = b.Get<DiffDeepBag>("memBacking").AsMemory();
            ImmutableArray<DiffDeepBag> immBags =
                ImmutableCollectionsMarshal.AsImmutableArray(b.Get<DiffDeepBag>("immBacking"));
            int[] outTotals = b.Get<int>("outTotals");
            MarshalingAliasingDifferentialKernels.DeepStructSixContainersKernel(
                arrBags, spanBags, roSpanBags, segBags, memBags, immBags, outTotals);
        },
        Launch = static (b, s) =>
        {
            DiffDeepBag[] arrBags = b.Get<DiffDeepBag>("arrBags");
            Span<DiffDeepBag> spanBags = b.Get<DiffDeepBag>("spanBacking").AsSpan();
            ReadOnlySpan<DiffDeepBag> roSpanBags = b.Get<DiffDeepBag>("roSpanBacking");
            ArraySegment<DiffDeepBag> segBags = new(b.Get<DiffDeepBag>("segBacking"), offset: 1, count: 2);
            Memory<DiffDeepBag> memBags = b.Get<DiffDeepBag>("memBacking").AsMemory();
            ImmutableArray<DiffDeepBag> immBags =
                ImmutableCollectionsMarshal.AsImmutableArray(b.Get<DiffDeepBag>("immBacking"));
            int[] outTotals = b.Get<int>("outTotals");
            MarshalingAliasingDifferentialKernels.LaunchDeepStructSixContainersKernel(
                arrBags, spanBags, roSpanBags, segBags, memBags, immBags, outTotals,
                gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_primaryCtorDeepStruct = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.PrimaryCtorDeepStructKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .InDeepScalar(
                "byVal",
                new DiffPrimaryCtorBag(2, new Vector2(1.5f, 0.5f), [10, 20]),
                ClonePrimary,
                ComparePrimary)
            .RefDeepScalar(
                "byRef",
                new DiffPrimaryCtorBag(3, new Vector2(-1.0f, 4.0f), [7, 8]),
                ClonePrimary,
                ComparePrimary)
            .InDeep(
                "arr",
                [new DiffPrimaryCtorBag(4, new Vector2(2.0f, 3.0f), [100, 200])],
                ClonePrimary,
                ComparePrimary)
            .Out<float>("outEval", 3, OutputPolicy.Bitwise),
        Oracle = static (b, _) =>
        {
            DiffPrimaryCtorBag byVal = b.GetScalar<DiffPrimaryCtorBag>("byVal");
            ref DiffPrimaryCtorBag byRef = ref b.GetScalarRef<DiffPrimaryCtorBag>("byRef");
            DiffPrimaryCtorBag[] arr = b.Get<DiffPrimaryCtorBag>("arr");
            float[] outEval = b.Get<float>("outEval");
            MarshalingAliasingDifferentialKernels.PrimaryCtorDeepStructKernel(byVal, ref byRef, arr, outEval);
        },
        Launch = static (b, s) =>
        {
            DiffPrimaryCtorBag byVal = b.GetScalar<DiffPrimaryCtorBag>("byVal");
            ref DiffPrimaryCtorBag byRef = ref b.GetScalarRef<DiffPrimaryCtorBag>("byRef");
            DiffPrimaryCtorBag[] arr = b.Get<DiffPrimaryCtorBag>("arr");
            float[] outEval = b.Get<float>("outEval");
            MarshalingAliasingDifferentialKernels.LaunchPrimaryCtorDeepStructKernel(
                byVal, ref byRef, arr, outEval, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    /// <summary>
    /// Deep struct with nested deep struct arguments passed by value, in, and ref.
    /// </summary>
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DeepStructMarshaling_NestedAllArgModes_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_deepStructAllArgModes);

    /// <summary>
    /// Deep struct instances marshaled across 6 container shapes: array, Span, ReadOnlySpan,
    /// ArraySegment, Memory, and ImmutableArray.
    /// </summary>
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DeepStructMarshaling_SixContainerShapes_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_deepStructSixContainers);

    /// <summary>
    /// C# 12 primary constructor deep struct marshaled across value, ref, and array arguments.
    /// </summary>
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DeepStructMarshaling_PrimaryConstructor_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_primaryCtorDeepStruct);
}
