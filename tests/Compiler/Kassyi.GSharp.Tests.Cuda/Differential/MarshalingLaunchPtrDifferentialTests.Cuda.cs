#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public partial class MarshalingLaunchPtrDifferentialTests
{
    private static partial Action<DiffBuffers, LaunchShape> GetPtrLengthGuardedScaleLaunch() =>
        static (b, s) =>
        {
            using var scope = b.PinOrUpload();
            MarshalingAliasingDifferentialKernels.LaunchPtrLengthGuardedScaleKernelPtr(
                scope.GetArray("data"), 3, scope.GetArray("outLen"), gridDimX: s.GridX, blockDimX: s.BlockX);
        };

    private static partial Action<DiffBuffers, LaunchShape>? GetOverloadedLaunchPtr() =>
        static (b, s) =>
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
        };

    private static partial Action<DiffBuffers, LaunchShape>? GetPtrConvertStructAndRefLaunchPtr() =>
        static (b, s) =>
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
        };

    // --- Launch{Kernel}Ptr passes a null array that is not optional, as the array launcher passes one: the kernel's
    // read of it throws, as in C# ---
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void LaunchPtr_NullNonOptionalArray_ThrowsAtTheRead()
    {
        CudaCapability.EnsureGpuAvailable();
        const int rows = 4;
        int[] src = [1, 2, 3, 4];
        int[] dst = new int[rows];

        using var ctxScope = new GpuKernelLoader.CudaContextScope();
        using GpuBuffer<int> dSrc = new(rows);
        using GpuBuffer<int> dDst = new(rows);
        dSrc.CopyToDevice(src);

        // A pointer launch reports a device error at the next synchronization
        Assert.Throws<NullReferenceException>(() =>
        {
            BoundsAndMemoryDifferentialKernels.LaunchNullArrayOptionalValidKernelPtr(
                default, dDst.ToProxy(), rows, blockDimX: rows);
            GpuKernelLoader.SynchronizeDevice();
        });

        // A null optional array is null, which the kernel tests before it reads it
        BoundsAndMemoryDifferentialKernels.LaunchNullArrayOptionalValidKernelPtr(
            dSrc.ToProxy(), dDst.ToProxy(), rows, default, blockDimX: rows);
        dDst.CopyToHost(dst);
        Assert.Equal(src, dst);
    }
}
