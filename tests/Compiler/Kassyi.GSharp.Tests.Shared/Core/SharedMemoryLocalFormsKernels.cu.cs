// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// The forms of a <c>SharedMemory&lt;T&gt;</c> local that are not a direct <c>new</c> (#1085): a parenthesized
/// creation, a copy of another local and the result of a device function. Each thread writes and reads its own
/// element, so no barrier is needed and the kernels stay plain functions on the CPU paths.
/// </summary>
public static partial class SharedMemoryLocalFormsKernels
{
    [GpuDeviceFunction]
    public static SharedMemory<int> Identity(SharedMemory<int> memory) => memory;

    [GpuKernel]
    public static void ParenthesizedCreationKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = (new SharedMemory<int>(8));
        s[tid] = tid + 1;
        output[tid] = s[tid];
    }

    [GpuKernel]
    public static void CopyOfLocalKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = new SharedMemory<int>(8);
        var alias = s;
        alias[tid] = 10 + tid;
        output[tid] = s[tid];
    }

    [GpuKernel]
    public static void DeviceFunctionResultKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var local = new SharedMemory<int>(8);
        SharedMemory<int> returned = Identity(local);
        returned[tid] = 20 + tid;
        output[tid] = local[tid];
    }
}
