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
}
