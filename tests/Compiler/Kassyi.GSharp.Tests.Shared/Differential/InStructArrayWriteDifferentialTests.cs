#nullable enable
using System;
using Kassyi.GSharp.Core;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.MarshalingDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests verifying that writing elements of an array held by an <c>in</c> or <c>ref readonly</c> struct
/// compiles and matches C# across managed, native CPU, and CUDA.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class InStructArrayWriteDifferentialTests
{
    private static string KernelName(string kernelMemberName, string? suffix = null) =>
        DiffKernelNames.Of(typeof(InStructArrayWriteDifferentialKernels), kernelMemberName, suffix);

    internal static readonly DifferentialCase s_deviceInStructArrayWrite = new(
        KernelName(nameof(InStructArrayWriteDifferentialKernels.DeviceInStructArrayWriteKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .InDeepScalar(
                "bag",
                new DiffDeepBag { Data = [1, 2, 3, 4], Tag = 7 },
                CloneDeepBag,
                CompareDeepBag)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            DiffDeepBag bag = b.GetScalar<DiffDeepBag>("bag");
            InStructArrayWriteDifferentialKernels.LaunchDeviceInStructArrayWriteKernel(
                bag, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_deviceInSpanForEachRefWrite = new(
        KernelName(nameof(InStructArrayWriteDifferentialKernels.DeviceInSpanForEachRefWriteKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .InDeepScalar(
                "bag",
                new DiffDeepBag { Data = [1, 2, 3], Tag = 5 },
                CloneDeepBag,
                CompareDeepBag)
            .Out<int>("observed", 3, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            DiffDeepBag bag = b.GetScalar<DiffDeepBag>("bag");
            InStructArrayWriteDifferentialKernels.LaunchDeviceInSpanForEachRefWriteKernel(
                bag, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_refReadOnlyLocalStructArrayWrite = new(
        KernelName(nameof(InStructArrayWriteDifferentialKernels.RefReadOnlyLocalStructArrayWriteKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .InDeepScalar(
                "bag",
                new DiffDeepBag { Data = [1, 2, 3, 4], Tag = 9 },
                CloneDeepBag,
                CompareDeepBag)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            DiffDeepBag bag = b.GetScalar<DiffDeepBag>("bag");
            InStructArrayWriteDifferentialKernels.LaunchRefReadOnlyLocalStructArrayWriteKernel(
                bag, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_deviceInStructFixedArrayWrite = new(
        KernelName(nameof(InStructArrayWriteDifferentialKernels.DeviceInStructFixedArrayWriteKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .InDeepScalar(
                "bag",
                new DiffDeepBag { Data = [1, 2, 3, 4], Tag = 11 },
                CloneDeepBag,
                CompareDeepBag)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            DiffDeepBag bag = b.GetScalar<DiffDeepBag>("bag");
            InStructArrayWriteDifferentialKernels.LaunchDeviceInStructFixedArrayWriteKernel(
                bag, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceInStruct_ArrayElementWrite_MatchesReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_deviceInStructArrayWrite);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceInSpan_ForEachRefWrite_MatchesReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_deviceInSpanForEachRefWrite);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void RefReadOnlyLocal_StructArrayElementWrite_MatchesReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_refReadOnlyLocalStructArrayWrite);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void DeviceInStruct_FixedArrayPointerWrite_MatchesReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_deviceInStructFixedArrayWrite);

    private static DiffDeepBag[] CloneDeepBags(DiffDeepBag[] src, DiffArrayCloneScope scope)
    {
        var copy = new DiffDeepBag[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            copy[i] = CloneDeepBag(src[i], scope);
        }

        return copy;
    }

    private static DiffConvertedBag CloneConvertedBag(DiffConvertedBag src, DiffArrayCloneScope scope) =>
        new()
        {
            Packs = scope.CloneArray(src.Packs) ?? Array.Empty<DiffBoolPack>(),
            Nested = CloneDeepBags(src.Nested, scope),
            Tag = src.Tag,
        };

    private static void CompareConvertedBag(StructFieldCheckContext ctx, DiffConvertedBag o, DiffConvertedBag a)
    {
        ctx.Field(nameof(DiffConvertedBag.Tag), o.Tag, a.Tag, OutputPolicy.Exact);
        ctx.Array(
            nameof(DiffConvertedBag.Packs),
            o.Packs,
            a.Packs,
            OutputPolicy.ForStruct<DiffBoolPack>(static (itemCtx, ep, ap) =>
            {
                itemCtx.Field(nameof(DiffBoolPack.Flag0), ep.Flag0, ap.Flag0, OutputPolicy.Exact);
                itemCtx.Field(nameof(DiffBoolPack.Mid), ep.Mid, ap.Mid, OutputPolicy.Exact);
                itemCtx.Field(nameof(DiffBoolPack.Flag1), ep.Flag1, ap.Flag1, OutputPolicy.Exact);
            }));
        ctx.Array(
            nameof(DiffConvertedBag.Nested),
            o.Nested,
            a.Nested,
            OutputPolicy.ForStruct<DiffDeepBag>(CompareDeepBag));
    }

    internal static readonly DifferentialCase s_standaloneInBagArrayWrite = new(
        KernelName(nameof(InStructArrayWriteDifferentialKernels.StandaloneInBagArrayWriteKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .InDeepScalar(
                "bag",
                new DiffDeepBag { Data = [1, 2, 3, 4], Tag = 13 },
                CloneDeepBag,
                CompareDeepBag)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            DiffDeepBag bag = b.GetScalar<DiffDeepBag>("bag");
            InStructArrayWriteDifferentialKernels.LaunchStandaloneInBagArrayWriteKernel(
                bag, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
            Assert.Equal(13, bag.Tag);
        },
    };

    internal static readonly DifferentialCase s_inBagBesideRefBag = new(
        KernelName(nameof(InStructArrayWriteDifferentialKernels.InBagBesideRefBagKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .InDeepScalar(
                "readOnlyBag",
                new DiffDeepBag { Data = [1, 2, 3, 4], Tag = 17 },
                CloneDeepBag,
                CompareDeepBag)
            .RefDeepScalar(
                "mutableBag",
                new DiffDeepBag { Data = [10, 20, 30, 40], Tag = 100 },
                CloneDeepBag,
                CompareDeepBag)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            ref DiffDeepBag readOnlyBag = ref b.GetScalarRef<DiffDeepBag>("readOnlyBag");
            ref DiffDeepBag mutableBag = ref b.GetScalarRef<DiffDeepBag>("mutableBag");
            InStructArrayWriteDifferentialKernels.LaunchInBagBesideRefBagKernel(
                in readOnlyBag, ref mutableBag, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
            Assert.Equal(17, readOnlyBag.Tag);
        },
    };

    internal static readonly DifferentialCase s_aliasedInAndRefSameBag = new(
        KernelName(nameof(InStructArrayWriteDifferentialKernels.AliasedInAndRefSameBagKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .RefDeepScalar(
                "bag",
                new DiffDeepBag { Data = [2, 4, 6, 8], Tag = 23 },
                CloneDeepBag,
                CompareDeepBag)
            .Out<int>("observed", 2, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            ref DiffDeepBag bag = ref b.GetScalarRef<DiffDeepBag>("bag");
            InStructArrayWriteDifferentialKernels.LaunchAliasedInAndRefSameBagKernel(
                in bag, ref bag, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
        },
    };

    internal static readonly DifferentialCase s_twoAliasedInBagsSameVar = new(
        KernelName(nameof(InStructArrayWriteDifferentialKernels.TwoAliasedInBagsKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .InDeepScalar(
                "bag",
                new DiffDeepBag { Data = [1, 2, 3, 4], Tag = 29 },
                CloneDeepBag,
                CompareDeepBag)
            .InDeep(
                "sink",
                [new DiffDeepBag { Data = [99], Tag = 88 }],
                CloneDeepBag,
                CompareDeepBag)
            .Out<int>("observed", 3, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            ref DiffDeepBag bag = ref b.GetScalarRef<DiffDeepBag>("bag");
            InStructArrayWriteDifferentialKernels.LaunchTwoAliasedInBagsKernel(
                in bag, in bag, b.Get<DiffDeepBag>("sink"), b.Get<int>("observed"),
                gridDimX: s.GridX, blockDimX: s.BlockX);
            Assert.Equal(29, bag.Tag);
        },
    };

    internal static readonly DifferentialCase s_inConvertedElementArrayBag = new(
        KernelName(nameof(InStructArrayWriteDifferentialKernels.InConvertedElementArrayBagKernel)))
    {
        Shape = LaunchShapes.SingleThread(),
        Seeds = [0],
        Inputs = static (_, _) => new DiffBuffers()
            .InDeepScalar(
                "readOnlyBag",
                new DiffConvertedBag
                {
                    Packs = [new DiffBoolPack { Flag0 = false, Mid = 10, Flag1 = true }],
                    Nested = [new DiffDeepBag { Data = [5, 6], Tag = 7 }],
                    Tag = 31,
                },
                CloneConvertedBag,
                CompareConvertedBag)
            .RefDeepScalar(
                "mutableBag",
                new DiffConvertedBag
                {
                    Packs = [new DiffBoolPack { Flag0 = true, Mid = 20, Flag1 = false }],
                    Nested = [new DiffDeepBag { Data = [15, 16], Tag = 17 }],
                    Tag = 41,
                },
                CloneConvertedBag,
                CompareConvertedBag)
            .Out<int>("observed", 3, OutputPolicy.Exact),
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            ref DiffConvertedBag readOnlyBag = ref b.GetScalarRef<DiffConvertedBag>("readOnlyBag");
            ref DiffConvertedBag mutableBag = ref b.GetScalarRef<DiffConvertedBag>("mutableBag");
            InStructArrayWriteDifferentialKernels.LaunchInConvertedElementArrayBagKernel(
                in readOnlyBag, ref mutableBag, b.Get<int>("observed"), gridDimX: s.GridX, blockDimX: s.BlockX);
            Assert.Equal(31, readOnlyBag.Tag);
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void StandaloneInBag_ArrayElementWrite_MatchesReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_standaloneInBagArrayWrite);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void InBagBesideRefBag_ArrayWriteLeavesInScalarUnchanged_MatchesReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_inBagBesideRefBag);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void AliasedInAndRefSameBag_WriteThroughInShowsAndPersists_MatchesReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_aliasedInAndRefSameBag);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void TwoAliasedInBags_WriteBackAllInGroup_MatchesReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_twoAliasedInBagsSameVar);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void InConvertedElementArrayBag_UnmarshalsConvertedElementsWithoutMutatingTag_MatchesReferenceOnEveryPath() =>
        DifferentialRunner.Run(s_inConvertedElementArrayBag);
}
