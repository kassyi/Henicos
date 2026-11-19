// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Kernels exercising <c>out SharedMemory&lt;T&gt;</c> declaration expressions and discard arguments.
/// </summary>
public static partial class SharedMemoryOutKernels
{
    [GpuDeviceFunction]
    public static void PickOut(SharedMemory<int> a, out SharedMemory<int> result)
    {
        result = a;
    }

    [GpuKernel]
    public static void OutVarKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var a = new SharedMemory<int>(8);
        PickOut(a, out var o);
        o[tid] = tid + 1;
        output[tid] = a[tid];
    }

    [GpuKernel]
    public static void OutTypedKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var a = new SharedMemory<int>(8);
        PickOut(a, out SharedMemory<int> o);
        o[tid] = tid + 1;
        output[tid] = a[tid];
    }

    [GpuKernel]
    public static void OutDiscardKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var a = new SharedMemory<int>(8);
        PickOut(a, out _);
        a[tid] = tid + 1;
        output[tid] = a[tid];
    }
}
