#nullable enable
using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
#if GSHARP_CUDA
using Kassyi.GSharp.Runtime;
#endif
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class MarshalingAliasingDifferentialTests
{
    private static readonly IElementComparer<DiffVec2Padded> Vec2PaddedComparer =
        new StructElementComparer<DiffVec2Padded>(static (ctx, o, a) =>
        {
            ctx.Field(nameof(DiffVec2Padded.A), o.A, a.A, OutputPolicy.Bitwise);
            ctx.Field("V.X", o.V.X, a.V.X, OutputPolicy.Bitwise);
            ctx.Field("V.Y", o.V.Y, a.V.Y, OutputPolicy.Bitwise);
        });

    private static DiffDeepBag CloneDeepBag(DiffDeepBag src, DiffArrayCloneScope scope)
        => new()
        {
            Data = scope.CloneArray(src.Data) ?? Array.Empty<int>(),
            Tag = src.Tag,
        };

    private static void CompareDeepBag(StructFieldCheckContext ctx, DiffDeepBag o, DiffDeepBag a)
    {
        ctx.Field("Tag", o.Tag, a.Tag);
        ctx.Field("Data.Length", o.Data.Length, a.Data.Length);
        int len = Math.Min(o.Data.Length, a.Data.Length);
        for (int i = 0; i < len; i++)
        {
            ctx.Field($"Data[{i}]", o.Data[i], a.Data[i]);
        }
    }

    private static DiffNestedDeep CloneNestedDeep(DiffNestedDeep src, DiffArrayCloneScope scope)
        => new()
        {
            Indices = scope.CloneArray(src.Indices) ?? Array.Empty<int>(),
            Inner = new DiffInnerBag
            {
                Weights = scope.CloneArray(src.Inner.Weights) ?? Array.Empty<float>(),
                Bias = src.Inner.Bias,
            },
            Scale = src.Scale,
        };

    private static void CompareNestedDeep(StructFieldCheckContext ctx, DiffNestedDeep o, DiffNestedDeep a)
    {
        ctx.Field("Scale", o.Scale, a.Scale);
        ctx.Field("Inner.Bias", o.Inner.Bias, a.Inner.Bias);
        ctx.Field("Indices.Length", o.Indices.Length, a.Indices.Length);
        int idxLen = Math.Min(o.Indices.Length, a.Indices.Length);
        for (int i = 0; i < idxLen; i++)
        {
            ctx.Field($"Indices[{i}]", o.Indices[i], a.Indices[i]);
        }

        ctx.Field("Inner.Weights.Length", o.Inner.Weights.Length, a.Inner.Weights.Length);
        int wLen = Math.Min(o.Inner.Weights.Length, a.Inner.Weights.Length);
        for (int i = 0; i < wLen; i++)
        {
            ctx.Field($"Inner.Weights[{i}]", o.Inner.Weights[i], a.Inner.Weights[i], OutputPolicy.Bitwise);
        }
    }

    // --- D3-M7a: Deep Struct with nested Deep Struct across value, in, ref ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DeepStruct_NestedAllArgModes_MatchesSequentialOracle()
    {
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.DeepStructAllArgModesKernel)}")
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
        });
    }

    // --- D3-M7b: Deep Struct across all 6 array-like container shapes ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DeepStruct_SixContainerShapes_MatchesSequentialOracle()
    {
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.DeepStructSixContainersKernel)}")
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
                DiffDeepBag[] immBacking = [new DiffDeepBag { Data = [60, 61], Tag = 7 }, new DiffDeepBag { Data = [62, 63], Tag = 8 }];

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
                ImmutableArray<DiffDeepBag> immBags = ImmutableCollectionsMarshal.AsImmutableArray(b.Get<DiffDeepBag>("immBacking"));
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
                ImmutableArray<DiffDeepBag> immBags = ImmutableCollectionsMarshal.AsImmutableArray(b.Get<DiffDeepBag>("immBacking"));
                int[] outTotals = b.Get<int>("outTotals");
                MarshalingAliasingDifferentialKernels.LaunchDeepStructSixContainersKernel(
                    arrBags, spanBags, roSpanBags, segBags, memBags, immBags, outTotals,
                    gridDimX: s.GridX, blockDimX: s.BlockX);
            },
        });
    }

    // --- D3-M7c: C# 12 Primary Constructor Deep Struct (#513) ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DeepStruct_PrimaryConstructor_MatchesSequentialOracle()
    {
        static DiffPrimaryCtorBag ClonePrimary(DiffPrimaryCtorBag src, DiffArrayCloneScope scope)
            => new(src.Scale, src.Bias, scope.CloneArray(src.Data) ?? Array.Empty<int>());

        static void ComparePrimary(StructFieldCheckContext ctx, DiffPrimaryCtorBag o, DiffPrimaryCtorBag a)
        {
            ctx.Field("Scale", o.Scale, a.Scale);
            ctx.Field("Bias.X", o.Bias.X, a.Bias.X, OutputPolicy.Bitwise);
            ctx.Field("Bias.Y", o.Bias.Y, a.Bias.Y, OutputPolicy.Bitwise);
            ctx.Field("Data.Length", o.Data.Length, a.Data.Length);
            int len = Math.Min(o.Data.Length, a.Data.Length);
            for (int i = 0; i < len; i++)
            {
                ctx.Field($"Data[{i}]", o.Data[i], a.Data[i]);
            }
        }

        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.PrimaryCtorDeepStructKernel)}")
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            Inputs = static (_, _) => new DiffBuffers()
                .InDeepScalar("byVal", new DiffPrimaryCtorBag(2, new Vector2(1.5f, 0.5f), [10, 20]), ClonePrimary, ComparePrimary)
                .RefDeepScalar("byRef", new DiffPrimaryCtorBag(3, new Vector2(-1.0f, 4.0f), [7, 8]), ClonePrimary, ComparePrimary)
                .InDeep("arr", [new DiffPrimaryCtorBag(4, new Vector2(2.0f, 3.0f), [100, 200])], ClonePrimary, ComparePrimary)
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
        });
    }

    // --- D3-A1: Top-level array aliasing across 4 attribute combinations (#721) + overlapping Span (#857) + aliased ref scalar ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Aliasing_TopLevelArrayCombinations_MatchesSequentialOracle()
    {
        const int n = 16;

        // 1. (int[] a, int[] b) passed the exact same int[] instance
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.AliasTwoRwArraysKernel)}")
        {
            Shape = LaunchShapes.Grid1D(n, blockX: 16),
            Seeds = [0],
            Inputs = static (_, _) =>
            {
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
        });

        // 2. ([In] int[] aIn, int[] bRw) passed the exact same int[] instance
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.AliasInAndRwArrayKernel)}")
        {
            Shape = LaunchShapes.Grid1D(n, blockX: 16),
            Seeds = [0],
            Inputs = static (_, _) =>
            {
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
        });

        // 3. ([Out] int[] aOut, [In] int[] bIn) passed the exact same int[] instance
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.AliasOutAndInArrayKernel)}")
        {
            Shape = LaunchShapes.Grid1D(n, blockX: 16),
            Seeds = [0],
            Inputs = static (_, _) =>
            {
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
        });

        // 4. ([In] int[] aIn, [In] int[] bIn) passed the exact same int[] instance (caller array must remain untouched)
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.AliasTwoInArraysKernel)}")
        {
            Shape = LaunchShapes.Grid1D(n, blockX: 16),
            Seeds = [0],
            Inputs = static (_, _) =>
            {
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
                // Reference model clones the [In] buffer once so both aIn and bIn alias the same shadow copy without mutating caller 'shared'
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
        });

        // 5. Partially overlapping Span<int> and ReadOnlySpan<int> slices from the same backing array (#857 on CUDA)
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.OverlappingSpanSliceKernel)}")
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
        });

        // 6. Aliased ref int scalar parameters passed the same variable (Issue: 0 // ND3-1 on CUDA)
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.AliasedRefScalarsKernel)}")
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
        });
    }

    // --- D3-A2: Deep Struct internal array aliasing (#707) ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Aliasing_DeepStructInternalArraySharing_MatchesSequentialOracle()
    {
        static DiffArrayPair ClonePair(DiffArrayPair src, DiffArrayCloneScope scope)
            => new()
            {
                First = scope.CloneArray(src.First) ?? Array.Empty<int>(),
                Second = scope.CloneArray(src.Second) ?? Array.Empty<int>(),
            };

        static void ComparePair(StructFieldCheckContext ctx, DiffArrayPair o, DiffArrayPair a)
        {
            ctx.Field("First[0]", o.First[0], a.First[0]);
            ctx.Field("Second[0]", o.Second[0], a.Second[0]);
        }

        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.DeepStructInternalAliasKernel)}")
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            Inputs = static (_, _) =>
            {
                int[] sharedPairArr = [5, 6];
                int[] sharedBagArr = [50, 60];
                int[] sharedElemArr = [500, 600];

                return new DiffBuffers()
                    .RefDeepScalar("pair", new DiffArrayPair { First = sharedPairArr, Second = sharedPairArr }, ClonePair, ComparePair)
                    .InDeepScalar("bagA", new DiffDeepBag { Data = sharedBagArr, Tag = 1 }, CloneDeepBag, CompareDeepBag)
                    .InDeepScalar("bagB", new DiffDeepBag { Data = sharedBagArr, Tag = 2 }, CloneDeepBag, CompareDeepBag)
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
        });
    }

    // --- D3-A3: Cross-level aliasing between top-level array and Deep Struct field ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Aliasing_CrossLevelTopAndDeepStruct_MatchesSequentialOracle()
    {
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.CrossLevelTopAndDeepStructKernel)}")
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
                    .InDeepScalar("bagByVal", new DiffDeepBag { Data = sharedRoAndVal, Tag = 1 }, CloneDeepBag, CompareDeepBag)
                    .RefDeepScalar("bagByRef", new DiffDeepBag { Data = sharedRwAndRef, Tag = 2 }, CloneDeepBag, CompareDeepBag)
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
        });
    }

    // --- D3-A4: Cross-aliasing between [In] DeepStruct[] element field and writable int[] (#985 / S18) ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_Aliasing_CrossAliasInDeepArrayAndRwArray_MatchesSequentialOracle()
    {
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.CrossAliasInDeepArrayAndRwArrayKernel)}")
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
        });
    }

    // --- D3-D1: [In] array mutation isolation ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DirectionAttributes_InArrayMutationIsolation_ProtectsCallerBuffers()
    {
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.InArrayMutationIsolationKernel)}")
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            Inputs = static (_, _) =>
            {
                int[] inPrim = [10, 20];
                DiffVec2Padded[] inConv =
                [
                    new DiffVec2Padded { A = 1.0f, V = new Vector2(2.0f, 3.0f) },
                ];
                DiffNestedDeep[] inDeep =
                [
                    new DiffNestedDeep
                    {
                        Indices = [1, 2],
                        Inner = new DiffInnerBag { Weights = [5.0f, 6.0f], Bias = 7 },
                        Scale = 8,
                    },
                ];

                return new DiffBuffers()
                    .In("inPrimitives", inPrim, OutputPolicy.Exact, readOnly: true)
                    .In("inConvertStructs", inConv, OutputPolicy.ForStruct(Vec2PaddedComparer), Vec2PaddedComparer, readOnly: true)
                    .InDeep("inDeepStructs", inDeep, CloneNestedDeep, CompareNestedDeep, readOnly: true)
                    .Out<float>("outObserved", 3, OutputPolicy.Bitwise);
            },
            Reference = static (b, s) =>
            {
                int[] primCopy = (int[])b.Get<int>("inPrimitives").Clone();
                DiffVec2Padded[] convCopy = (DiffVec2Padded[])b.Get<DiffVec2Padded>("inConvertStructs").Clone();
                DiffNestedDeep[] inDeep = b.Get<DiffNestedDeep>("inDeepStructs");
                DiffArrayCloneScope scope = new(new System.Collections.Generic.Dictionary<object, object>(ReferenceEqualityComparer.Instance));
                DiffNestedDeep[] deepCopy = new DiffNestedDeep[inDeep.Length];
                for (int i = 0; i < inDeep.Length; i++)
                {
                    deepCopy[i] = CloneNestedDeep(inDeep[i], scope);
                }

                float[] outObserved = b.Get<float>("outObserved");
                SequentialOracle.Run(s, ThreadOrder.Forward, () =>
                    MarshalingAliasingDifferentialKernels.InArrayMutationIsolationKernel(primCopy, convCopy, deepCopy, outObserved));
            },
            Launch = static (b, s) =>
            {
                int[] inPrim = b.Get<int>("inPrimitives");
                DiffVec2Padded[] inConv = b.Get<DiffVec2Padded>("inConvertStructs");
                DiffNestedDeep[] inDeep = b.Get<DiffNestedDeep>("inDeepStructs");
                float[] outObserved = b.Get<float>("outObserved");
                MarshalingAliasingDifferentialKernels.LaunchInArrayMutationIsolationKernel(
                    inPrim, inConv, inDeep, outObserved, gridDimX: s.GridX, blockDimX: s.BlockX);
            },
        });
    }

    // --- D3-D2: [Out] array full write vs partial write (Q10 = a) ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_DirectionAttributes_OutArrayFullAndPartialWrite_MatchesSequentialOracle()
    {
        const int n = 64;
        OutputPolicy convertPolicy = OutputPolicy.ForStruct(Vec2PaddedComparer);

        // (a) Full write (writeStride = 1)
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.OutArrayStridedWriteKernel)}.Full")
        {
            Shape = LaunchShapes.Grid1D(n, blockX: 32),
            Seeds = [0],
            Inputs = (_, _) =>
            {
                int[] src = new int[n];
                for (int i = 0; i < n; i++)
                {
                    src[i] = i + 1;
                }

                return new DiffBuffers()
                    .In("src", src, OutputPolicy.Exact, readOnly: true)
                    .Out<int>("dstPrim", n, OutputPolicy.Exact)
                    .Out<DiffVec2Padded>("dstConvert", n, convertPolicy, Vec2PaddedComparer);
            },
            Oracle = static (b, _) =>
            {
                int[] src = b.Get<int>("src");
                int[] dstPrim = b.Get<int>("dstPrim");
                DiffVec2Padded[] dstConvert = b.Get<DiffVec2Padded>("dstConvert");
                MarshalingAliasingDifferentialKernels.OutArrayStridedWriteKernel(src, dstPrim, dstConvert, 1);
            },
            Launch = static (b, s) =>
            {
                int[] src = b.Get<int>("src");
                int[] dstPrim = b.Get<int>("dstPrim");
                DiffVec2Padded[] dstConvert = b.Get<DiffVec2Padded>("dstConvert");
                MarshalingAliasingDifferentialKernels.LaunchOutArrayStridedWriteKernel(
                    src, dstPrim, dstConvert, 1, gridDimX: s.GridX, blockDimX: s.BlockX);
            },
        });

        // (b) Partial write (writeStride = 2): per Q10 = a, compare only written even indices and ignore unwritten odd indices!
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.OutArrayStridedWriteKernel)}.Partial")
        {
            Shape = LaunchShapes.Grid1D(n, blockX: 32),
            Seeds = [0],
            Inputs = (_, _) =>
            {
                int[] src = new int[n];
                for (int i = 0; i < n; i++)
                {
                    src[i] = i + 1;
                }

                return new DiffBuffers()
                    .In("src", src, OutputPolicy.Exact, readOnly: true)
                    .Out<int>("dstPrim", rows: n / 2, columns: [("even_written", OutputPolicy.Exact), ("odd_unwritten", OutputPolicy.Ignore)])
                    .Out<DiffVec2Padded>("dstConvert", rows: n / 2, columns: [("even_written", convertPolicy), ("odd_unwritten", OutputPolicy.Ignore)], Vec2PaddedComparer);
            },
            Oracle = static (b, _) =>
            {
                int[] src = b.Get<int>("src");
                int[] dstPrim = b.Get<int>("dstPrim");
                DiffVec2Padded[] dstConvert = b.Get<DiffVec2Padded>("dstConvert");
                MarshalingAliasingDifferentialKernels.OutArrayStridedWriteKernel(src, dstPrim, dstConvert, 2);
            },
            Launch = static (b, s) =>
            {
                int[] src = b.Get<int>("src");
                int[] dstPrim = b.Get<int>("dstPrim");
                DiffVec2Padded[] dstConvert = b.Get<DiffVec2Padded>("dstConvert");
                MarshalingAliasingDifferentialKernels.LaunchOutArrayStridedWriteKernel(
                    src, dstPrim, dstConvert, 2, gridDimX: s.GridX, blockDimX: s.BlockX);
            },
        });
    }

    // --- D3-P1: Launch{Kernel}Ptr non-overloaded IntPtr with Length guard (#720) ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_LaunchPtr_NonOverloadedIntPtrWithLengthGuard_MatchesSequentialOracle()
    {
        const int n = 64;
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.PtrLengthGuardedScaleKernel)}")
        {
            Shape = LaunchShapes.Grid1D(n, blockX: 32),
            Seeds = [0],
            Inputs = static (_, _) =>
            {
                int[] data = new int[n];
                for (int i = 0; i < n; i++)
                {
                    data[i] = i + 1;
                }

                return new DiffBuffers()
                    .In("data", data, OutputPolicy.Exact)
                    .Out<int>("outLen", 1, OutputPolicy.Exact);
            },
            Oracle = static (b, _) =>
            {
                int[] data = b.Get<int>("data");
                int[] outLen = b.Get<int>("outLen");
                MarshalingAliasingDifferentialKernels.PtrLengthGuardedScaleKernel(data, 3, outLen);
            },
            Launch = static (b, s) =>
            {
                int[] data = b.Get<int>("data");
                int[] outLen = b.Get<int>("outLen");
#if GSHARP_CUDA
                using var scope = b.PinOrUpload();
                IntPtr pData = scope.PinOrUpload<int>("data");
                IntPtr pLen = scope.PinOrUpload<int>("outLen");
                MarshalingAliasingDifferentialKernels.LaunchPtrLengthGuardedScaleKernelPtr(
                    pData, 3, pLen, gridDimX: s.GridX, blockDimX: s.BlockX);
#else
                MarshalingAliasingDifferentialKernels.LaunchPtrLengthGuardedScaleKernel(
                    data, 3, outLen, gridDimX: s.GridX, blockDimX: s.BlockX);
#endif
            },
        });
    }

    // --- D3-P2: Launch{Kernel}Ptr with overloaded GpuBuffer<T> signature ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_LaunchPtr_OverloadedGpuBufferSignature_MatchesSequentialOracle()
    {
        const int n = 64;
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.OverloadedPtrKernel)}")
        {
            Shape = LaunchShapes.Grid1D(n, blockX: 32),
            Seeds = [0],
            Inputs = static (_, _) =>
            {
                int[] srcInt = new int[n];
                float[] srcFloat = new float[n];
                for (int i = 0; i < n; i++)
                {
                    srcInt[i] = i + 2;
                    srcFloat[i] = (i * 0.5f) + 1.0f;
                }

                return new DiffBuffers()
                    .In("srcInt", srcInt, OutputPolicy.Exact, readOnly: true)
                    .Out<int>("dstInt", n, OutputPolicy.Exact)
                    .In("srcFloat", srcFloat, OutputPolicy.Bitwise, readOnly: true)
                    .Out<float>("dstFloat", n, OutputPolicy.Bitwise);
            },
            Oracle = static (b, _) =>
            {
                int[] srcInt = b.Get<int>("srcInt");
                int[] dstInt = b.Get<int>("dstInt");
                float[] srcFloat = b.Get<float>("srcFloat");
                float[] dstFloat = b.Get<float>("dstFloat");
                MarshalingAliasingDifferentialKernels.OverloadedPtrKernel(srcInt, dstInt, n);
                MarshalingAliasingDifferentialKernels.OverloadedPtrKernel(srcFloat, dstFloat, n);
            },
            Launch = static (b, s) =>
            {
                int[] srcInt = b.Get<int>("srcInt");
                int[] dstInt = b.Get<int>("dstInt");
                float[] srcFloat = b.Get<float>("srcFloat");
                float[] dstFloat = b.Get<float>("dstFloat");
                MarshalingAliasingDifferentialKernels.LaunchOverloadedPtrKernel(
                    srcInt, dstInt, n, gridDimX: s.GridX, blockDimX: s.BlockX);
                MarshalingAliasingDifferentialKernels.LaunchOverloadedPtrKernel(
                    srcFloat, dstFloat, n, gridDimX: s.GridX, blockDimX: s.BlockX);
            },
#if GSHARP_CUDA
            LaunchPtr = static (b, s) =>
            {
                int[] srcInt = b.Get<int>("srcInt");
                int[] dstInt = b.Get<int>("dstInt");
                float[] srcFloat = b.Get<float>("srcFloat");
                float[] dstFloat = b.Get<float>("dstFloat");

                using var ctxScope = new GpuKernelLoader.CudaContextScope();
                using GpuBuffer<int> dSrcInt = new(n);
                using GpuBuffer<int> dDstInt = new(n);
                dSrcInt.CopyToDevice(srcInt);
                MarshalingAliasingDifferentialKernels.LaunchOverloadedPtrKernelPtr(
                    dSrcInt, dDstInt, n, gridDimX: s.GridX, blockDimX: s.BlockX);
                dDstInt.CopyToHost(dstInt);

                using GpuBuffer<float> dSrcFloat = new(n);
                using GpuBuffer<float> dDstFloat = new(n);
                dSrcFloat.CopyToDevice(srcFloat);
                MarshalingAliasingDifferentialKernels.LaunchOverloadedPtrKernelPtr(
                    dSrcFloat, dDstFloat, n, gridDimX: s.GridX, blockDimX: s.BlockX);
                dDstFloat.CopyToHost(dstFloat);
            },
#endif
        });
    }

    // --- D3-P3: Launch{Kernel}Ptr with ref Convert struct & Deep Struct NotSupportedException ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_LaunchPtr_RefConvertStructAndDeepStructContract_MatchesExpected()
    {
        const int n = 32;
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.PtrConvertStructAndRefKernel)}")
        {
            Shape = LaunchShapes.Grid1D(n, blockX: 32),
            Seeds = [0],
            Inputs = static (_, _) =>
            {
                int[] items = new int[n];
                for (int i = 0; i < n; i++)
                {
                    items[i] = i * 10;
                }

                return new DiffBuffers()
                    .In("items", items, OutputPolicy.Exact)
                    .RefScalar(
                        "accum",
                        new DiffVec2Padded { A = 5.0f, V = new Vector2(3.0f, -2.0f) },
                        OutputPolicy.ForStruct(Vec2PaddedComparer),
                        Vec2PaddedComparer);
            },
            Oracle = static (b, _) =>
            {
                int[] items = b.Get<int>("items");
                ref DiffVec2Padded accum = ref b.GetScalarRef<DiffVec2Padded>("accum");
                MarshalingAliasingDifferentialKernels.PtrConvertStructAndRefKernel(items, ref accum, n);
            },
            Launch = static (b, s) =>
            {
                int[] items = b.Get<int>("items");
                ref DiffVec2Padded accum = ref b.GetScalarRef<DiffVec2Padded>("accum");
                MarshalingAliasingDifferentialKernels.LaunchPtrConvertStructAndRefKernel(
                    items, ref accum, n, gridDimX: s.GridX, blockDimX: s.BlockX);
            },
#if GSHARP_CUDA
            LaunchPtr = static (b, s) =>
            {
                int[] items = b.Get<int>("items");
                ref DiffVec2Padded accum = ref b.GetScalarRef<DiffVec2Padded>("accum");
                MarshalingAliasingDifferentialKernels.AotContexts.DiffVec2Padded nativeAccum = default;
                MarshalingAliasingDifferentialKernels.AotContexts.Marshal_DiffVec2Padded(
                    ref accum, ref nativeAccum, isDevicePtr: true, deviceArrays: null, hStream: IntPtr.Zero);

                using var ctxScope = new GpuKernelLoader.CudaContextScope();
                using GpuBuffer<int> dItems = new(n);
                using GpuBuffer<MarshalingAliasingDifferentialKernels.AotContexts.DiffVec2Padded> dAccum = new(1);
                dItems.CopyToDevice(items);
                dAccum.CopyToDevice(new ReadOnlySpan<MarshalingAliasingDifferentialKernels.AotContexts.DiffVec2Padded>(ref nativeAccum));
                MarshalingAliasingDifferentialKernels.LaunchPtrConvertStructAndRefKernelPtr(
                    dItems.DevicePointer, dAccum.DevicePointer, n, gridDimX: s.GridX, blockDimX: s.BlockX);
                dItems.CopyToHost(items);
                dAccum.CopyToHost(new Span<MarshalingAliasingDifferentialKernels.AotContexts.DiffVec2Padded>(ref nativeAccum));
                MarshalingAliasingDifferentialKernels.AotContexts.Unmarshal_DiffVec2Padded(
                    ref accum, ref nativeAccum, isDevicePtr: true, hStream: IntPtr.Zero);
            },
#endif
        });

#if GSHARP_CUDA
        CudaCapability.EnsureGpuAvailable();
        var deepBag = new DiffDeepBag { Data = [1, 2], Tag = 3 };
        MarshalingAliasingDifferentialKernels.AotContexts.DiffDeepBag nativeBag = default;
        Assert.Throws<NotSupportedException>(() =>
            MarshalingAliasingDifferentialKernels.AotContexts.Marshal_DiffDeepBag(
                ref deepBag, ref nativeBag, isDevicePtr: true, deviceArrays: null, hStream: IntPtr.Zero));
#endif
    }

    // --- D3-N1: Non-nullable array passed null (ArgumentNullException) or empty (ArgumentException) ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.SadPath)]
    public void Test_NullAndEmptyArrays_NonNullableContract_ThrowsExpectedArgumentExceptions()
    {
        // (a) Passing null to non-nullable int[] throws ArgumentNullException on Managed/Native/Cuda
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.NonNullableArrayKernel)}.Null")
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            ExceptionPolicy = ExceptionPolicy.For<ArgumentNullException>(
                DiffPath.Managed | DiffPath.Native,
                paramName: "input"),
            Inputs = static (_, _) => new DiffBuffers()
                .InNullable<int>("input", null)
                .Out<int>("output", 1, OutputPolicy.Ignore),
            Launch = static (b, s) =>
            {
                int[]? input = b.GetNullable<int>("input");
                int[] output = b.Get<int>("output");
#pragma warning disable CS8604
                MarshalingAliasingDifferentialKernels.LaunchNonNullableArrayKernel(
                    input, output, gridDimX: s.GridX, blockDimX: s.BlockX);
#pragma warning restore CS8604
            },
        });

        // (b) Passing empty [] to non-nullable int[] throws ArgumentException on Managed/Native/Cuda
        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.NonNullableArrayKernel)}.Empty")
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            ExceptionPolicy = ExceptionPolicy.For<ArgumentException>(
                DiffPath.Managed | DiffPath.Native,
                paramName: "input"),
            Inputs = static (_, _) => new DiffBuffers()
                .In("input", Array.Empty<int>(), OutputPolicy.Ignore)
                .Out<int>("output", 1, OutputPolicy.Ignore),
            Launch = static (b, s) =>
            {
                int[] input = b.Get<int>("input");
                int[] output = b.Get<int>("output");
                MarshalingAliasingDifferentialKernels.LaunchNonNullableArrayKernel(
                    input, output, gridDimX: s.GridX, blockDimX: s.BlockX);
            },
        });

#if GSHARP_CUDA
        CudaCapability.EnsureGpuAvailable();
        int[] dummyOut = new int[1];
#pragma warning disable CS8625
        var nullEx = Assert.Throws<ArgumentNullException>(() =>
            MarshalingAliasingDifferentialKernels.LaunchNonNullableArrayKernel(null, dummyOut));
#pragma warning restore CS8625
        Assert.Equal("input", nullEx.ParamName);

        var emptyEx = Assert.Throws<ArgumentException>(() =>
            MarshalingAliasingDifferentialKernels.LaunchNonNullableArrayKernel(Array.Empty<int>(), dummyOut));
        Assert.Equal("input", emptyEx.ParamName);
#endif
    }

    // --- D3-N2: Nullable top-level array null vs empty (NullReadsAsEmpty #806, #838) ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullAndEmptyArrays_NullableTopLevelNullReadsAsEmpty_MatchesAcrossBackends()
    {
        (string Label, int[]? OptPrim, DiffDeepBag[]? OptDeep)[] table =
        [
            ("both_null", null, null),
            ("both_empty", Array.Empty<int>(), Array.Empty<DiffDeepBag>()),
            ("populated", [10, 20], [new DiffDeepBag { Data = [30, 40], Tag = 5 }]),
        ];

        var bagComparer = new StructElementComparer<DiffDeepBag>(CompareDeepBag);

        foreach ((string label, int[]? optPrimInit, DiffDeepBag[]? optDeepInit) in table)
        {
            DifferentialRunner.Run(new DifferentialCase(
                $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.NullableTopLevelArrayKernel)}.{label}")
            {
                Shape = LaunchShapes.SingleThread(),
                Seeds = [0],
                Inputs = (_, _) => new DiffBuffers()
                    .InNullable("optPrim", optPrimInit, OutputPolicy.Exact, readOnly: true)
                    .InNullable("optDeep", optDeepInit, OutputPolicy.ForStruct(bagComparer), bagComparer, readOnly: true)
                    .Out<int>("output", 6, OutputPolicy.Exact),
                Oracle = static (b, _) =>
                {
                    int[]? optPrim = b.GetNullable<int>("optPrim");
                    DiffDeepBag[]? optDeep = b.GetNullable<DiffDeepBag>("optDeep");
                    int[] output = b.Get<int>("output");
                    int tid = (BlockIndex.X * BlockDimension.X) + ThreadIndex.X;
                    if (tid == 0)
                    {
                        // Per G# top-level nullable array contract (#806, #838), both null and Length == 0 normalize to the null/empty slice (data == null, length == 0)
                        bool primNullOrEmpty = optPrim is null || optPrim.Length == 0;
                        bool deepNullOrEmpty = optDeep is null || optDeep.Length == 0;
                        output[0] = primNullOrEmpty ? -1 : 1;
                        output[1] = primNullOrEmpty ? -1 : 1;
                        output[2] = optPrim?.Length ?? 0;
                        output[3] = deepNullOrEmpty ? -1 : 1;
                        output[4] = optDeep?.Length ?? 0;
                        output[5] = (optDeep is not null && optDeep.Length > 0 && optDeep[0].Data is not null && optDeep[0].Data.Length > 0)
                            ? optDeep[0].Data[0] + optDeep[0].Tag
                            : -99;
                    }
                },
                Launch = static (b, s) =>
                {
                    int[]? optPrim = b.GetNullable<int>("optPrim");
                    DiffDeepBag[]? optDeep = b.GetNullable<DiffDeepBag>("optDeep");
                    int[] output = b.Get<int>("output");
                    MarshalingAliasingDifferentialKernels.LaunchNullableTopLevelArrayKernel(
                        optPrim, optDeep, output, gridDimX: s.GridX, blockDimX: s.BlockX);
                },
            });
        }
    }

    // --- D3-N3: Struct field null array vs empty array (#706 EmptyArrayData + ArrayPool reuse) ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.Boundary)]
    public void Test_NullAndEmptyArrays_StructFieldNullVsEmpty_DistinguishesNullFromEmpty()
    {
        static DiffNullableDeepBag CloneNullableBag(DiffNullableDeepBag src, DiffArrayCloneScope scope)
            => new()
            {
                Data = scope.CloneArray(src.Data),
                Fallback = src.Fallback,
            };

        static void CompareNullableBag(StructFieldCheckContext ctx, DiffNullableDeepBag o, DiffNullableDeepBag a)
        {
            ctx.Field("Fallback", o.Fallback, a.Fallback);
            ctx.Field("DataIsNull", o.Data is null, a.Data is null);
            if (o.Data is not null && a.Data is not null)
            {
                ctx.Field("Data.Length", o.Data.Length, a.Data.Length);
                int len = Math.Min(o.Data.Length, a.Data.Length);
                for (int i = 0; i < len; i++)
                {
                    ctx.Field($"Data[{i}]", o.Data[i], a.Data[i]);
                }
            }
        }

        (string Label, int[]? ValData, int[]? RefData)[] cases =
        [
            ("warmup_populated", [10, 20, 30], [40, 50]),
            ("null_vs_empty", null, Array.Empty<int>()),
            ("empty_vs_null", Array.Empty<int>(), null),
        ];

        foreach ((string label, int[]? valData, int[]? refData) in cases)
        {
            DifferentialRunner.Run(new DifferentialCase(
                $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.StructFieldNullOrEmptySafeKernel)}.{label}")
            {
                Shape = LaunchShapes.SingleThread(),
                Seeds = [0],
                Inputs = (_, _) => new DiffBuffers()
                    .InDeepScalar("bagByVal", new DiffNullableDeepBag { Data = valData, Fallback = 7 }, CloneNullableBag, CompareNullableBag)
                    .RefDeepScalar("bagByRef", new DiffNullableDeepBag { Data = refData, Fallback = 11 }, CloneNullableBag, CompareNullableBag)
                    .Out<int>("output", 5, OutputPolicy.Exact),
                Oracle = static (b, _) =>
                {
                    DiffNullableDeepBag bagByVal = b.GetScalar<DiffNullableDeepBag>("bagByVal");
                    ref DiffNullableDeepBag bagByRef = ref b.GetScalarRef<DiffNullableDeepBag>("bagByRef");
                    int[] output = b.Get<int>("output");
                    MarshalingAliasingDifferentialKernels.StructFieldNullOrEmptySafeKernel(
                        bagByVal, ref bagByRef, output);
                },
                Launch = static (b, s) =>
                {
                    DiffNullableDeepBag bagByVal = b.GetScalar<DiffNullableDeepBag>("bagByVal");
                    ref DiffNullableDeepBag bagByRef = ref b.GetScalarRef<DiffNullableDeepBag>("bagByRef");
                    int[] output = b.Get<int>("output");
                    MarshalingAliasingDifferentialKernels.LaunchStructFieldNullOrEmptySafeKernel(
                        bagByVal, ref bagByRef, output, gridDimX: s.GridX, blockDimX: s.BlockX);
                },
            });
        }
    }

    // --- D3-N4: Struct field null array dereference throws NullReferenceException (#706) ---
    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Differential)]
    [Scenario(TestScenario.SadPath)]
    public void Test_NullAndEmptyArrays_StructFieldNullDereference_ThrowsNullReferenceExceptionOnCpu()
    {
        static DiffNullableDeepBag CloneNullBag(DiffNullableDeepBag src, DiffArrayCloneScope scope)
            => new()
            {
                Data = scope.CloneArray(src.Data),
                Fallback = src.Fallback,
            };

        DifferentialRunner.Run(new DifferentialCase(
            $"{nameof(MarshalingAliasingDifferentialKernels)}.{nameof(MarshalingAliasingDifferentialKernels.StructFieldNullDereferenceKernel)}")
        {
            Shape = LaunchShapes.SingleThread(),
            Seeds = [0],
            // CUDA device error channel (#716 / #729) is not yet wired to NullReferenceException
            ExceptionPolicy = ExceptionPolicy.For<NullReferenceException>(DiffPath.Managed | DiffPath.Native),
            Inputs = static (_, _) => new DiffBuffers()
                .InDeepScalar("bag", new DiffNullableDeepBag { Data = null, Fallback = 5 }, CloneNullBag)
                .Out<int>("output", 1, OutputPolicy.Ignore),
            Launch = static (b, s) =>
            {
                DiffNullableDeepBag bag = b.GetScalar<DiffNullableDeepBag>("bag");
                int[] output = b.Get<int>("output");
                MarshalingAliasingDifferentialKernels.LaunchStructFieldNullDereferenceKernel(
                    bag, output, gridDimX: s.GridX, blockDimX: s.BlockX);
            },
        });
    }
}
