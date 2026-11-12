#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.MarshalingDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for array and struct aliasing across read/write modes,
/// overlapping Span slices, aliased ref scalars, and deep struct internal arrays.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class MarshalingAliasingDifferentialTests
{
    private static DiffArrayPair ClonePair(DiffArrayPair src, DiffArrayCloneScope scope)
        => new()
        {
            First = scope.CloneArray(src.First) ?? Array.Empty<int>(),
            Second = scope.CloneArray(src.Second) ?? Array.Empty<int>(),
        };

    private static void ComparePair(StructFieldCheckContext ctx, DiffArrayPair o, DiffArrayPair a)
    {
        ctx.Field("First[0]", o.First[0], a.First[0]);
        ctx.Field("Second[0]", o.Second[0], a.Second[0]);
    }

    // --- Top-level array aliasing across 4 attribute combinations (#721)
    // + overlapping Span (#857) + aliased ref scalar (#1061) ---
    internal static readonly DifferentialCase s_aliasTwoRwArrays = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.AliasTwoRwArraysKernel)))
    {
        Shape = LaunchShapes.Linear(16, blockX: 16),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            const int n = 16;
            int[] shared = new int[n];
            for (int i = 0; i < n; i++)
            {
                shared[i] = i + 1;
            }

            return new DiffBuffers()
                .In("shared", shared, OutputPolicy.Exact)
                .Out<int>("observed", n, OutputPolicy.Exact);
        },
        Oracle = static (b, _) =>
        {
            int[] shared = b.Get<int>("shared");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.AliasTwoRwArraysKernel(shared, shared, observed);
        },
        Launch = static (b, s) =>
        {
            int[] shared = b.Get<int>("shared");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.LaunchAliasTwoRwArraysKernel(
                shared, shared, observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_aliasInAndRwArray = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.AliasInAndRwArrayKernel)))
    {
        Shape = LaunchShapes.Linear(16, blockX: 16),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            const int n = 16;
            int[] shared = new int[n];
            for (int i = 0; i < n; i++)
            {
                shared[i] = (i * 2) + 3;
            }

            return new DiffBuffers()
                .In("shared", shared, OutputPolicy.Exact)
                .Out<int>("observed", n, OutputPolicy.Exact);
        },
        Oracle = static (b, _) =>
        {
            int[] shared = b.Get<int>("shared");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.AliasInAndRwArrayKernel(shared, shared, observed);
        },
        Launch = static (b, s) =>
        {
            int[] shared = b.Get<int>("shared");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.LaunchAliasInAndRwArrayKernel(
                shared, shared, observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_aliasOutAndInArray = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.AliasOutAndInArrayKernel)))
    {
        Shape = LaunchShapes.Linear(16, blockX: 16),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            const int n = 16;
            int[] shared = new int[n];
            for (int i = 0; i < n; i++)
            {
                shared[i] = (i * 5) + 10;
            }

            return new DiffBuffers()
                .In("shared", shared, OutputPolicy.Exact)
                .Out<int>("observed", n, OutputPolicy.Exact);
        },
        Oracle = static (b, _) =>
        {
            int[] shared = b.Get<int>("shared");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.AliasOutAndInArrayKernel(shared, shared, observed);
        },
        Launch = static (b, s) =>
        {
            int[] shared = b.Get<int>("shared");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.LaunchAliasOutAndInArrayKernel(
                shared, shared, observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_aliasTwoInArrays = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.AliasTwoInArraysKernel)))
    {
        Shape = LaunchShapes.Linear(16, blockX: 16),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            const int n = 16;
            int[] shared = new int[n];
            for (int i = 0; i < n; i++)
            {
                shared[i] = i + 100;
            }

            return new DiffBuffers()
                .In("shared", shared, OutputPolicy.Exact, readOnly: true)
                .Out<int>("observed", n, OutputPolicy.Exact);
        },
        Reference = static (b, s) =>
        {
            int[] sharedShadow = (int[])b.Get<int>("shared").Clone();
            int[] observed = b.Get<int>("observed");
            SequentialOracle.Run(s, ThreadOrder.Forward, () =>
                MarshalingAliasingDifferentialKernels.AliasTwoInArraysKernel(sharedShadow, sharedShadow, observed));
        },
        Launch = static (b, s) =>
        {
            int[] shared = b.Get<int>("shared");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.LaunchAliasTwoInArraysKernel(
                shared, shared, observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_overlappingSpanSlice = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.OverlappingSpanSliceKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .In("backing", [1, 2, 3, 4, 5, 6, 7, 8], OutputPolicy.Exact)
            .Out<int>("observed", 4, OutputPolicy.Exact),
        Oracle = static (b, _) =>
        {
            int[] backing = b.Get<int>("backing");
            Span<int> sliceA = backing.AsSpan(0, 4);
            Span<int> sliceB = backing.AsSpan(2, 4);
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.OverlappingSpanSliceKernel(sliceA, sliceB, observed);
        },
        Launch = static (b, s) =>
        {
            int[] backing = b.Get<int>("backing");
            Span<int> sliceA = backing.AsSpan(0, 4);
            Span<int> sliceB = backing.AsSpan(2, 4);
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.LaunchOverlappingSpanSliceKernel(
                sliceA, sliceB, observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_aliasedRefScalars = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.AliasedRefScalarsKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .RefScalar("sharedRef", 100)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        Oracle = static (b, _) =>
        {
            ref int sharedRef = ref b.GetScalarRef<int>("sharedRef");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.AliasedRefScalarsKernel(ref sharedRef, ref sharedRef, observed);
        },
        Launch = static (b, s) =>
        {
            ref int sharedRef = ref b.GetScalarRef<int>("sharedRef");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.LaunchAliasedRefScalarsKernel(
                ref sharedRef, ref sharedRef, observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    // Two raw pointers into one buffer, the second one element ahead: C# reads every value the previous iteration
    // wrote, so the buffer becomes 0, 1, 2, ..., n (#979)
    private static unsafe DifferentialCase CreateOverlappingPointerShiftCase() =>
        new(KernelName(nameof(MarshalingAliasingDifferentialKernels.OverlappingPointerShiftKernel)))
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            Inputs = static (_, _) => new DiffBuffers()
                .In("backing", new int[OverlappingPointerShiftCount + 1], OutputPolicy.Exact),
            Oracle = static (b, _) =>
            {
                fixed (int* backing = b.Get<int>("backing"))
                {
                    MarshalingAliasingDifferentialKernels.OverlappingPointerShiftKernel(
                        backing, backing + 1, OverlappingPointerShiftCount);
                }
            },
            Launch = static (b, s) =>
            {
                using var scope = b.PinOrUpload();
                var backing = (int*)scope.PinOrUpload<int>("backing");
                MarshalingAliasingDifferentialKernels.LaunchOverlappingPointerShiftKernel(
                    backing, backing + 1, OverlappingPointerShiftCount, gridDimX: s.GridX, blockDimX: s.BlockX);
            },
        };

    private const int OverlappingPointerShiftCount = 8;

    internal static readonly DifferentialCase s_overlappingPointerShift = CreateOverlappingPointerShiftCase();

    // --- Deep Struct internal array aliasing (#707) ---
    internal static readonly DifferentialCase s_deepStructInternalArraySharing = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.DeepStructInternalAliasKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            int[] sharedPairArr = [5, 6];
            int[] sharedBagArr = [50, 60];
            int[] sharedElemArr = [500, 600];

            return new DiffBuffers()
                .RefDeepScalar(
                    "pair",
                    new DiffArrayPair { First = sharedPairArr, Second = sharedPairArr },
                    ClonePair,
                    ComparePair)
                .InDeepScalar(
                    "bagA",
                    new DiffDeepBag { Data = sharedBagArr, Tag = 1 },
                    CloneDeepBag,
                    CompareDeepBag)
                .InDeepScalar(
                    "bagB",
                    new DiffDeepBag { Data = sharedBagArr, Tag = 2 },
                    CloneDeepBag,
                    CompareDeepBag)
                .InDeep(
                    "bagArray",
                    [
                        new DiffDeepBag { Data = sharedElemArr, Tag = 10 },
                        new DiffDeepBag { Data = sharedElemArr, Tag = 20 },
                    ],
                    CloneDeepBag,
                    CompareDeepBag)
                .Out<int>("observed", 4, OutputPolicy.Exact);
        },
        Oracle = static (b, _) =>
        {
            ref DiffArrayPair pair = ref b.GetScalarRef<DiffArrayPair>("pair");
            DiffDeepBag bagA = b.GetScalar<DiffDeepBag>("bagA");
            DiffDeepBag bagB = b.GetScalar<DiffDeepBag>("bagB");
            DiffDeepBag[] bagArray = b.Get<DiffDeepBag>("bagArray");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.DeepStructInternalAliasKernel(
                ref pair, bagA, bagB, bagArray, observed);
        },
        Launch = static (b, s) =>
        {
            ref DiffArrayPair pair = ref b.GetScalarRef<DiffArrayPair>("pair");
            DiffDeepBag bagA = b.GetScalar<DiffDeepBag>("bagA");
            DiffDeepBag bagB = b.GetScalar<DiffDeepBag>("bagB");
            DiffDeepBag[] bagArray = b.Get<DiffDeepBag>("bagArray");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.LaunchDeepStructInternalAliasKernel(
                ref pair, bagA, bagB, bagArray, observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    // --- Cross-level aliasing between top-level array and Deep Struct field ---
    internal static readonly DifferentialCase s_crossLevelTopAndDeepStruct = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.CrossLevelTopAndDeepStructKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            int[] sharedRoAndVal = [10, 20];
            int[] sharedRwAndRef = [100, 200];

            return new DiffBuffers()
                .In("topReadOnly", sharedRoAndVal, OutputPolicy.Exact)
                .In("topWritable", sharedRwAndRef, OutputPolicy.Exact)
                .InDeepScalar(
                    "bagByVal",
                    new DiffDeepBag { Data = sharedRoAndVal, Tag = 1 },
                    CloneDeepBag,
                    CompareDeepBag)
                .RefDeepScalar(
                    "bagByRef",
                    new DiffDeepBag { Data = sharedRwAndRef, Tag = 2 },
                    CloneDeepBag,
                    CompareDeepBag)
                .Out<int>("observed", 4, OutputPolicy.Exact);
        },
        Oracle = static (b, _) =>
        {
            int[] topReadOnly = b.Get<int>("topReadOnly");
            int[] topWritable = b.Get<int>("topWritable");
            DiffDeepBag bagByVal = b.GetScalar<DiffDeepBag>("bagByVal");
            ref DiffDeepBag bagByRef = ref b.GetScalarRef<DiffDeepBag>("bagByRef");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.CrossLevelTopAndDeepStructKernel(
                topReadOnly, topWritable, bagByVal, ref bagByRef, observed);
        },
        Launch = static (b, s) =>
        {
            int[] topReadOnly = b.Get<int>("topReadOnly");
            int[] topWritable = b.Get<int>("topWritable");
            DiffDeepBag bagByVal = b.GetScalar<DiffDeepBag>("bagByVal");
            ref DiffDeepBag bagByRef = ref b.GetScalarRef<DiffDeepBag>("bagByRef");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.LaunchCrossLevelTopAndDeepStructKernel(
                topReadOnly, topWritable, bagByVal, ref bagByRef, observed,
                gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    // --- Cross-aliasing between [In] DeepStruct[] element field and writable int[] (#985) ---
    internal static readonly DifferentialCase s_crossAliasInDeepArrayAndRwArray = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.CrossAliasInDeepArrayAndRwArrayKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            int[] shared = [42, 43];
            return new DiffBuffers()
                .InDeep("inBags", [new DiffDeepBag { Data = shared, Tag = 7 }], CloneDeepBag, CompareDeepBag)
                .In("rwArr", shared, OutputPolicy.Exact)
                .Out<int>("observed", 2, OutputPolicy.Exact);
        },
        Oracle = static (b, _) =>
        {
            DiffDeepBag[] inBags = b.Get<DiffDeepBag>("inBags");
            int[] rwArr = b.Get<int>("rwArr");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.CrossAliasInDeepArrayAndRwArrayKernel(inBags, rwArr, observed);
        },
        Launch = static (b, s) =>
        {
            DiffDeepBag[] inBags = b.Get<DiffDeepBag>("inBags");
            int[] rwArr = b.Get<int>("rwArr");
            int[] observed = b.Get<int>("observed");
            MarshalingAliasingDifferentialKernels.LaunchCrossAliasInDeepArrayAndRwArrayKernel(
                inBags, rwArr, observed, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ArrayAliasing_TwoReadWriteArrays_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_aliasTwoRwArrays);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ArrayAliasing_InAndReadWriteArrays_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_aliasInAndRwArray);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ArrayAliasing_OutAndInArrays_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_aliasOutAndInArray);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ArrayAliasing_TwoInArrays_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_aliasTwoInArrays);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SpanSliceAliasing_OverlappingSlices_MatchesOracleExceptKnownCudaIssue() =>
        DifferentialRunner.Run(s_overlappingSpanSlice);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ScalarAliasing_AliasedRefScalars_MatchesOracleExceptKnownCudaIssue() =>
        DifferentialRunner.Run(s_aliasedRefScalars);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void PointerAliasing_DestinationOneAheadOfSource_MatchesTheOracleOnEveryPath() =>
        DifferentialRunner.Run(s_overlappingPointerShift);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DeepStructAliasing_InternalArraySharing_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_deepStructInternalArraySharing);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DeepStructAliasing_CrossLevelTopAndDeepStruct_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_crossLevelTopAndDeepStruct);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void DeepStructAliasing_CrossAliasInDeepArrayAndRwArray_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_crossAliasInDeepArrayAndRwArray);
}
