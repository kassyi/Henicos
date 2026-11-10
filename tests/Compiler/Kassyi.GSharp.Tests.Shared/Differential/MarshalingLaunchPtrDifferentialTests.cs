#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Core;
#if GSHARP_CUDA
using Kassyi.GSharp.Runtime;
#endif
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
public sealed class MarshalingLaunchPtrDifferentialTests
{
    private const int PtrLengthGuardedScaleCount = 64;

    private static readonly DifferentialCase s_ptrLengthGuardedScale = new(
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
            MarshalingAliasingDifferentialKernels.LaunchPtrLengthGuardedScaleKernelPtr(
                scope.GetArray("data"), 3, scope.GetArray("outLen"), gridDimX: s.GridX, blockDimX: s.BlockX);
#else
            MarshalingAliasingDifferentialKernels.LaunchPtrLengthGuardedScaleKernel(
                data, 3, outLen, gridDimX: s.GridX, blockDimX: s.BlockX);
#endif
        },
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LaunchPtr_NonOverloadedIntPtrWithLengthGuard_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_ptrLengthGuardedScale);

#if GSHARP_CUDA
    // --- Launch{Kernel}Ptr rejects a null array that is not optional before the launch, as the array launcher does;
    // the kernel leaves out the null check of an array parameter it never writes on that promise (#1409) ---
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void LaunchPtr_NullNonOptionalArray_ThrowsArgumentNullExceptionBeforeTheLaunch()
    {
        CudaCapability.EnsureGpuAvailable();
        const int rows = 4;
        int[] src = [1, 2, 3, 4];
        int[] dst = new int[rows];

        using var ctxScope = new GpuKernelLoader.CudaContextScope();
        using GpuBuffer<int> dSrc = new(rows);
        using GpuBuffer<int> dDst = new(rows);
        dSrc.CopyToDevice(src);

        var srcNull = Assert.Throws<ArgumentNullException>(() =>
            BoundsAndMemoryDifferentialKernels.LaunchNullArrayOptionalValidKernelPtr(
                default, dDst.ToProxy(), rows, blockDimX: rows));
        var dstNull = Assert.Throws<ArgumentNullException>(() =>
            BoundsAndMemoryDifferentialKernels.LaunchNullArrayOptionalValidKernelPtr(
                dSrc.ToProxy(), default, rows, blockDimX: rows));
        Assert.Equal("src", srcNull.ParamName);
        Assert.Equal("dst", dstNull.ParamName);

        // A null optional array is not rejected and reads as empty; the launch after the rejected ones also shows that
        // nothing reached the device
        BoundsAndMemoryDifferentialKernels.LaunchNullArrayOptionalValidKernelPtr(
            dSrc.ToProxy(), dDst.ToProxy(), rows, default, blockDimX: rows);
        dDst.CopyToHost(dst);
        Assert.Equal(src, dst);
    }
#endif

    // --- Launch{Kernel}Ptr with overloaded GpuBuffer<T> signature ---
    private const int OverloadedPtrCount = 64;

    private static readonly DifferentialCase s_overloadedPtr = new(
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
        Oracle = static (b, _) =>
        {
            int[] srcInt = b.Get<int>("srcInt");
            int[] dstInt = b.Get<int>("dstInt");
            float[] srcFloat = b.Get<float>("srcFloat");
            float[] dstFloat = b.Get<float>("dstFloat");
            MarshalingAliasingDifferentialKernels.OverloadedPtrKernel(srcInt, dstInt, OverloadedPtrCount);
            MarshalingAliasingDifferentialKernels.OverloadedPtrKernel(srcFloat, dstFloat, OverloadedPtrCount);
        },
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
#if GSHARP_CUDA
        LaunchPtr = static (b, s) =>
        {
            int[] srcInt = b.Get<int>("srcInt");
            int[] dstInt = b.Get<int>("dstInt");
            float[] srcFloat = b.Get<float>("srcFloat");
            float[] dstFloat = b.Get<float>("dstFloat");

            using var ctxScope = new GpuKernelLoader.CudaContextScope();
            using GpuBuffer<int> dSrcInt = new(OverloadedPtrCount);
            using GpuBuffer<int> dDstInt = new(OverloadedPtrCount);
            dSrcInt.CopyToDevice(srcInt);
            MarshalingAliasingDifferentialKernels.LaunchOverloadedPtrKernelPtr(
                dSrcInt, dDstInt, OverloadedPtrCount, gridDimX: s.GridX, blockDimX: s.BlockX);
            dDstInt.CopyToHost(dstInt);

            using GpuBuffer<float> dSrcFloat = new(OverloadedPtrCount);
            using GpuBuffer<float> dDstFloat = new(OverloadedPtrCount);
            dSrcFloat.CopyToDevice(srcFloat);
            MarshalingAliasingDifferentialKernels.LaunchOverloadedPtrKernelPtr(
                dSrcFloat, dDstFloat, OverloadedPtrCount, gridDimX: s.GridX, blockDimX: s.BlockX);
            dDstFloat.CopyToHost(dstFloat);
        },
#endif
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LaunchPtr_OverloadedGpuBufferSignature_MatchesSequentialOracle() =>
        DifferentialRunner.Run(s_overloadedPtr);

    // --- Launch{Kernel}Ptr with ref Convert struct ---
    private const int PtrConvertStructAndRefCount = 32;

    private static readonly DifferentialCase s_ptrConvertStructAndRef = new(
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
        Oracle = static (b, _) =>
        {
            int[] items = b.Get<int>("items");
            ref DiffVec2Padded accum = ref b.GetScalarRef<DiffVec2Padded>("accum");
            MarshalingAliasingDifferentialKernels.PtrConvertStructAndRefKernel(
                items, ref accum, PtrConvertStructAndRefCount);
        },
        Launch = static (b, s) =>
        {
            int[] items = b.Get<int>("items");
            ref DiffVec2Padded accum = ref b.GetScalarRef<DiffVec2Padded>("accum");
            MarshalingAliasingDifferentialKernels.LaunchPtrConvertStructAndRefKernel(
                items, ref accum, PtrConvertStructAndRefCount, gridDimX: s.GridX, blockDimX: s.BlockX);
        },
#if GSHARP_CUDA
        LaunchPtr = static (b, s) =>
        {
            int[] items = b.Get<int>("items");
            ref DiffVec2Padded accum = ref b.GetScalarRef<DiffVec2Padded>("accum");
            MarshalingAliasingDifferentialKernels.AotContexts.DiffVec2Padded nativeAccum = default;
            MarshalingAliasingDifferentialKernels.AotContexts.Marshal_DiffVec2Padded(
                ref accum, ref nativeAccum, deviceArrays: null, hStream: IntPtr.Zero);

            using var ctxScope = new GpuKernelLoader.CudaContextScope();
            using GpuBuffer<int> dItems = new(PtrConvertStructAndRefCount);
            using GpuBuffer<MarshalingAliasingDifferentialKernels.AotContexts.DiffVec2Padded> dAccum = new(1);
            dItems.CopyToDevice(items);
            dAccum.CopyToDevice(
                new ReadOnlySpan<MarshalingAliasingDifferentialKernels.AotContexts.DiffVec2Padded>(ref nativeAccum));
            MarshalingAliasingDifferentialKernels.LaunchPtrConvertStructAndRefKernelPtr(
                dItems.ToProxy(),
                dAccum.DevicePointer,
                PtrConvertStructAndRefCount,
                gridDimX: s.GridX,
                blockDimX: s.BlockX);
            dItems.CopyToHost(items);
            dAccum.CopyToHost(
                new Span<MarshalingAliasingDifferentialKernels.AotContexts.DiffVec2Padded>(ref nativeAccum));
            MarshalingAliasingDifferentialKernels.AotContexts.Unmarshal_DiffVec2Padded(
                ref accum, ref nativeAccum, hStream: IntPtr.Zero);
        },
#endif
    };

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LaunchPtr_RefConvertStruct_MatchesExpected() =>
        DifferentialRunner.Run(s_ptrConvertStructAndRef);
}
