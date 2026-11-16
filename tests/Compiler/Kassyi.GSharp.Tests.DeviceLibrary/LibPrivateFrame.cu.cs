using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.DeviceLibrary;

/// <summary>Device functions whose managed frame is private to this class, which another assembly runs through
/// the generated entries.</summary>
public static partial class LibPrivateFrame
{
    private struct Carry
    {
        public int V;
    }

    /// <summary>Keeps a private struct across the barrier and reads the next thread's slot.</summary>
    [GpuDeviceFunction]
    public static int KeepAcrossBarrier(int[] buffer, int tid)
    {
        var carry = new Carry();
        carry.V = tid * 3;
        buffer[tid] = tid + 1;
        Gpu.SyncThreads();
        return buffer[(tid + 1) % BlockDimension.X] + carry.V;
    }

    /// <summary>Private only through the frame of the call it holds.</summary>
    [GpuDeviceFunction]
    public static int Relay(int[] buffer, int tid) => KeepAcrossBarrier(buffer, tid) * 2 + 1;
}
