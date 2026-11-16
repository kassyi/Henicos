#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;
using Xunit;
using static Kassyi.GSharp.Tests.Shared.Differential.MarshalingDifferentialTestHelpers;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential tests for pointer launch signatures (<c>Launch{Kernel}Ptr</c>)
/// with length guards, overloads, and convert structs.
/// </summary>
[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public partial class MarshalingLaunchPtrDifferentialTests
{
    private const int PtrLengthGuardedScaleCount = 64;

    internal static readonly DifferentialCase s_ptrLengthGuardedScale = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.PtrLengthGuardedScaleKernel)))
    {
        Shape = LaunchShapes.Linear(PtrLengthGuardedScaleCount, blockX: 32),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            int[] data = new int[PtrLengthGuardedScaleCount];
            for (int i = 0; i < PtrLengthGuardedScaleCount; i++)
            {
                data[i] = i + 1;
            }

            return new DiffBuffers()
                .In("data", data, OutputPolicy.Exact)
                .Out<int>("outLen", 1, OutputPolicy.Exact);
        },
        Oracle = static (b, s) => MarshalingAliasingDifferentialKernels.LaunchPtrLengthGuardedScaleKernel(
            b.Get<int>("data"), 3, b.Get<int>("outLen"), gridDimX: s.GridX, blockDimX: s.BlockX),
        Launch = GetPtrLengthGuardedScaleLaunch(),
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LaunchPtr_NonOverloadedIntPtrWithLengthGuard_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_ptrLengthGuardedScale);

    // --- Launch{Kernel}Ptr with overloaded GpuBuffer<T> signature ---
    private const int OverloadedPtrCount = 64;

    internal static readonly DifferentialCase s_overloadedPtr = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.OverloadedPtrKernel)))
    {
        Shape = LaunchShapes.Linear(OverloadedPtrCount, blockX: 32),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            int[] srcInt = new int[OverloadedPtrCount];
            float[] srcFloat = new float[OverloadedPtrCount];
            for (int i = 0; i < OverloadedPtrCount; i++)
            {
                srcInt[i] = i + 2;
                srcFloat[i] = (i * 0.5f) + 1.0f;
            }

            return new DiffBuffers()
                .In("srcInt", srcInt, OutputPolicy.Exact, readOnly: true)
                .Out<int>("dstInt", OverloadedPtrCount, OutputPolicy.Exact)
                .In("srcFloat", srcFloat, OutputPolicy.Bitwise, readOnly: true)
                .Out<float>("dstFloat", OverloadedPtrCount, OutputPolicy.Bitwise);
        },
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] srcInt = b.Get<int>("srcInt");
            int[] dstInt = b.Get<int>("dstInt");
            float[] srcFloat = b.Get<float>("srcFloat");
            float[] dstFloat = b.Get<float>("dstFloat");
            MarshalingAliasingDifferentialKernels.LaunchOverloadedPtrKernel(
                srcInt, dstInt, OverloadedPtrCount, gridDimX: s.GridX, blockDimX: s.BlockX);
            MarshalingAliasingDifferentialKernels.LaunchOverloadedPtrKernel(
                srcFloat, dstFloat, OverloadedPtrCount, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
        LaunchPtr = GetOverloadedLaunchPtr(),
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LaunchPtr_OverloadedGpuBufferSignature_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_overloadedPtr);

    // --- Launch{Kernel}Ptr with ref Convert struct ---
    private const int PtrConvertStructAndRefCount = 32;

    internal static readonly DifferentialCase s_ptrConvertStructAndRef = new(
        KernelName(nameof(MarshalingAliasingDifferentialKernels.PtrConvertStructAndRefKernel)))
    {
        Shape = LaunchShapes.Linear(PtrConvertStructAndRefCount, blockX: 32),
        Seeds = [0],
        Inputs = static (_, _) =>
        {
            int[] items = new int[PtrConvertStructAndRefCount];
            for (int i = 0; i < PtrConvertStructAndRefCount; i++)
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
        OracleIsLaunch = true,
        Launch = static (b, s) =>
        {
            int[] items = b.Get<int>("items");
            ref DiffVec2Padded accum = ref b.GetScalarRef<DiffVec2Padded>("accum");
            MarshalingAliasingDifferentialKernels.LaunchPtrConvertStructAndRefKernel(
                items, ref accum, PtrConvertStructAndRefCount, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
        LaunchPtr = GetPtrConvertStructAndRefLaunchPtr(),
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LaunchPtr_RefConvertStruct_MatchesExpected() =>
        DifferentialRunner.Run(s_ptrConvertStructAndRef);

    private static partial Action<DiffBuffers, LaunchShape> GetPtrLengthGuardedScaleLaunch();
    private static partial Action<DiffBuffers, LaunchShape>? GetOverloadedLaunchPtr();
    private static partial Action<DiffBuffers, LaunchShape>? GetPtrConvertStructAndRefLaunchPtr();
}
