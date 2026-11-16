using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.DeviceLibrary;

/// <summary>A base class whose protected device function keeps a protected struct across a barrier; a kernel class
/// of another assembly that derives from it runs the function through the generated protected entries.</summary>
public partial class LibFrameBase
{
    protected LibFrameBase()
    {
    }

    protected struct Carry
    {
        public int V;
    }

    /// <summary>Keeps a protected struct across the barrier and reads the next thread's slot.</summary>
    [GpuDeviceFunction]
    protected static int KeepProtected(int[] buffer, int tid)
    {
        var carry = new Carry();
        carry.V = tid * 3;
        buffer[tid] = tid + 1;
        Gpu.SyncThreads();
        return buffer[(tid + 1) % BlockDimension.X] + carry.V;
    }
}
