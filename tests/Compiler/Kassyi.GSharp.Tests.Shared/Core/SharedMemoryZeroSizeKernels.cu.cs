// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// A <c>SharedMemory&lt;T&gt;</c> with a constant count of 0 is empty shared memory, like <c>new int[0]</c> (#1790): it
/// builds and runs on every path, and the declarations next to it keep their own memory.
/// </summary>
public static partial class SharedMemoryZeroSizeKernels
{
    private const int NoElements = 0;

    // An empty declaration of each kind next to one of 4 ints that the threads exchange across a barrier
    [GpuKernel]
    public static void EmptyBesideUsedKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var empty = new SharedMemory<int>(0);
        var emptyDoubles = new SharedMemory<double>(NoElements);
        var data = new SharedMemory<int>(4);
        data[tid] = 10 * (tid + 1);
        Gpu.SyncThreads();
        output[tid] = data[(tid + 1) % 4] + EmptyInDeviceFunction(tid);
    }

    // An empty declaration in a device function, called from every thread
    [GpuDeviceFunction]
    public static int EmptyInDeviceFunction(int tid)
    {
        var empty = new SharedMemory<long>(0);
        return tid;
    }

    // Reads the element 0 of an empty declaration: IndexOutOfRangeException, as for new int[0][0]
    [GpuKernel]
    public static void ReadEmptyKernel(int[] output)
    {
        var empty = new SharedMemory<int>(0);
        output[0] = empty[0];
    }
}
