using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.DeviceLibrary;
using Kassyi.GSharp.Tests.Shared.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>The kernels of <see cref="CSharpExecutionTests"/>, launched under <c>KernelExecution.CSharp</c>.</summary>
public static partial class CSharpExecutionKernels
{
    // Each thread takes the next slot with a plain read and write: a data race on the GPU, but one thread at a time in
    // C#, so the slots record the order in which the threads ran
    [GpuKernel]
    public static void RecordOrderKernel(int[] order, int[] next, int[] coordinates)
    {
        int id = LinearThreadIndex();
        int slot = next[0];
        next[0] = slot + 1;
        order[slot] = id;
        coordinates[id] = ThreadIndex.X + 10 * ThreadIndex.Y + 100 * BlockIndex.X + 1000 * BlockIndex.Y;
    }

    // Reads data[at[0]]: out of range when at[0] is past the end, which the C# body reports as C# does
    [GpuKernel]
    public static void ReadAtKernel(int[] data, int[] at, int[] result)
    {
        result[ThreadIndex.X] = data[at[0]];
    }

    // One value per thread, returned into the launcher's auto result
    [GpuKernel]
    public static int ReturnLinearKernel(int scale) => LinearThreadIndex() * scale;

    // A ref and an out scalar: the C# execution passes copies, written back after the last thread
    [GpuKernel]
    public static void RefOutKernel(ref int total, out int last)
    {
        total += 1;
        last = LinearThreadIndex();
    }

    // A span, which the C# execution passes through a pinned pointer
    [GpuKernel]
    public static void SpanKernel(Span<int> data, ReadOnlySpan<int> source)
    {
        int id = LinearThreadIndex();
        data[id] = source[id] * 3;
    }

    // A span of structs that hold an array: an element type that contains references
    [GpuKernel]
    public static void SpanBagKernel(Span<ConvertBag> bags)
    {
        int i = ThreadIndex.X;
        bags[i].N = bags[i].Data[0] + i;
    }

    // A value-returning kernel and a void kernel with the same parameters, whose launches allocate the same apart from
    // the result array
    [GpuKernel]
    public static int ReturnScaleKernel(int scale) => scale;

    [GpuKernel]
    public static void IgnoreScaleKernel(int scale)
    {
    }

    // Static shared memory in a device function of another assembly
    [GpuKernel]
    public static void LibrarySharedMemoryKernel(int[] data) => data[ThreadIndex.X] = LibMath.StaticTile(ThreadIndex.X);

    // A block barrier, which has no meaning when one thread runs at a time
    [GpuKernel]
    public static void BarrierKernel(int[] data)
    {
        int t = ThreadIndex.X;
        data[t] = t;
        Gpu.SyncThreads();
        data[t] += data[(t + 1) % BlockDimension.X];
    }

    // The barrier through a device function
    [GpuKernel]
    public static void BarrierThroughDeviceFunctionKernel(int[] data)
    {
        data[ThreadIndex.X] = Exchange(data, ThreadIndex.X);
    }

    // Block-shared memory, which the threads of a block share
    [GpuKernel]
    public static void SharedMemoryKernel(int[] data)
    {
        var tile = new SharedMemory<int>(32);
        tile[ThreadIndex.X] = data[ThreadIndex.X];
        data[ThreadIndex.X] = tile[ThreadIndex.X] + 1;
    }

    [GpuDeviceFunction]
    private static int Exchange(int[] data, int t)
    {
        Gpu.SyncThreads();
        return data[(t + 1) % BlockDimension.X];
    }

    [GpuDeviceFunction]
    private static int LinearThreadIndex() =>
        ((BlockIndex.Z * GridDimension.Y + BlockIndex.Y) * GridDimension.X + BlockIndex.X) *
        (BlockDimension.X * BlockDimension.Y * BlockDimension.Z) +
        (ThreadIndex.Z * BlockDimension.Y + ThreadIndex.Y) * BlockDimension.X + ThreadIndex.X;

    // Thread t writes result[2 t], then result[2 t + 1] = at[at[t]] / divisors[t]: an index out of range or a division
    // by zero ends thread t before that write
    [GpuKernel]
    public static void FailSomeThreadsKernel(int[] at, int[] divisors, int[] result)
    {
        int t = ThreadIndex.X;
        result[2 * t] = 1;
        result[2 * t + 1] = at[at[t]] / divisors[t];
    }
}
