using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.DeviceLibrary;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// Kernels that call the suspending device functions of LibPrivateFrame, whose managed frames hold a private nested
// struct of LibPrivateFrame and are run through the generated entries (#1991)
public static partial class CrossAssemblyPrivateFrameKernels
{
    [GpuKernel]
    public static void PrivateFrameKernel(int[] buffer, int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = LibPrivateFrame.KeepAcrossBarrier(buffer, tid);
    }

    [GpuKernel]
    public static void PrivateFrameRelayKernel(int[] buffer, int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = LibPrivateFrame.Relay(buffer, tid);
    }

    [GpuDeviceFunction]
    public static int AddHundred(int[] buffer, int tid) => LibPrivateFrame.KeepAcrossBarrier(buffer, tid) + 100;

    [GpuKernel]
    public static void PrivateFrameThroughFunctionKernel(int[] buffer, int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = AddHundred(buffer, tid);
    }
}
