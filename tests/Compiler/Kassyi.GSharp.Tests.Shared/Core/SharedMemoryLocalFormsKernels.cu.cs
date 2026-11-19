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

    [GpuDeviceFunction]
    public static SharedMemory<int> CreateInReturn()
    {
        return new SharedMemory<int>(8);
    }

    [GpuDeviceFunction]
    public static SharedMemory<int> CreateInExpressionBody() => new SharedMemory<int>(8);

    [GpuDeviceFunction]
    public static int StoreAndLoad(SharedMemory<int> memory, int index, int value)
    {
        memory[index] = value;
        return memory[index];
    }

    // The function ends with a barrier, so the CPU state machine splits it around the barrier
    [GpuDeviceFunction]
    public static SharedMemory<int> FillAcrossBarrier(int index)
    {
        var memory = new SharedMemory<int>(8);
        memory[index] = 100 + index;
        Gpu.SyncThreads();
        return memory;
    }

    [GpuKernel]
    public static void CreationInReturnKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = CreateInReturn();
        s[tid] = 30 + tid;
        output[tid] = s[tid];
    }

    [GpuKernel]
    public static void CreationInExpressionBodyKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = CreateInExpressionBody();
        s[tid] = 40 + tid;
        output[tid] = s[tid];
    }

    // A local function whose expression body creates the memory
    [GpuKernel]
    public static void CreationInLocalFunctionExpressionBodyKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = Create();
        s[tid] = 50 + tid;
        output[tid] = s[tid];

        SharedMemory<int> Create() => new SharedMemory<int>(8);
    }

    [GpuKernel]
    public static void CreationInArgumentKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = StoreAndLoad(new SharedMemory<int>(8), tid, 50 + tid);
    }

    [GpuKernel]
    public static void CreationInConditionalKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = tid >= 0 ? new SharedMemory<int>(8) : new SharedMemory<int>(16);
        s[tid] = 60 + tid;
        output[tid] = s[tid];
    }

    [GpuKernel]
    public static void BarrierFunctionResultKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = FillAcrossBarrier(tid);
        output[tid] = s[(tid + 1) % 8];
    }

    [GpuKernel]
    public static void CallResultAcrossBarrierKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = CreateInReturn();
        s[tid] = 110 + tid;
        Gpu.SyncThreads();
        output[tid] = s[(tid + 1) % 8];
    }

    [GpuKernel]
    public static void ConditionalOfLocalsKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var a = new SharedMemory<int>(8);
        var b = new SharedMemory<int>(8);
        var chosen = tid >= 0 ? a : b;
        chosen[tid] = 70 + tid;
        output[tid] = a[tid];
    }

    [GpuKernel]
    public static void DeviceFunctionTwiceKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var a = new SharedMemory<int>(8);
        var first = Identity(a);
        var second = Identity(first);
        second[tid] = 80 + tid;
        output[tid] = first[tid] + a[tid];
    }

    [GpuKernel]
    public static void AliasAcrossBarrierKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var s = new SharedMemory<int>(8);
        var alias = s;
        alias[tid] = 90 + tid;
        Gpu.SyncThreads();
        output[tid] = alias[(tid + 1) % 8];
    }

    [GpuKernel]
    public static void FloatAndByteAliasKernel(float[] floats, byte[] bytes)
    {
        int tid = ThreadIndex.X;
        var f = new SharedMemory<float>(8);
        var fAlias = f;
        fAlias[tid] = tid + 0.5f;
        floats[tid] = f[tid];
        var b = new SharedMemory<byte>(8);
        var bAlias = b;
        bAlias[tid] = (byte)(tid + 3);
        bytes[tid] = b[tid];
    }

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
