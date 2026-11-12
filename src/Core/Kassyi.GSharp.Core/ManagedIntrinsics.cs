using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Kassyi.GSharp.Core;

/// <summary>
/// The managed CPU path of the fence, timer, cache-hint, sleep, asynchronous copy and device heap intrinsics of
/// <c>Gpu</c>. The stubs generated from <c>src/NativeApi/ptx_intrinsics.yml</c> and <c>device_heap_intrinsics.yml</c>
/// forward here through their <c>managed</c> key; the integer and bit intrinsics forward to
/// <see cref="ManagedIntegerIntrinsics"/> and <see cref="ManagedBitIntrinsics"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each method gives a result that the GPU may give and that the native CPU prelude gives (<c>ThreadAndMemory.hpp</c>).
/// </para>
/// <para>Outside a managed launch each method throws <see cref="GpuOnlyException"/>, as the stub did.</para>
/// </remarks>
internal static class ManagedIntrinsics
{
    private const long NanosecondsPerSecond = 1_000_000_000L;

    // The threads of a managed launch share the host memory, so each fence scope is a full fence
    public static void ThreadFence()
    {
        Active(0);
        Interlocked.MemoryBarrier();
    }

    // Nanoseconds of the monotonic clock, as the native CPU prelude counts them; Clock keeps the low 32 bits
    public static uint Clock() => Active((uint)ReadNanoseconds());

    public static ulong Clock64() => Active(ReadNanoseconds());

    public static T LoadCachedGlobal<T>(ref T address) => Active(address);

    public static void StoreCachedGlobal<T>(ref T address, T val)
    {
        Active(0);
        address = val;
    }

    // A hint changes no value
    public static void Prefetch(IntPtr address) => Active(address);

    public static void ApplyPriorityL2(IntPtr address, uint bytes, float priority) => Active(address);

    // The GPU sleeps for a duration between 0 and twice the one asked for; 0 is one of them, and nothing else changes
    public static void NanoSleep(uint duration) => Active(duration);

    // The copy is done at once: the GPU makes it visible at the latest at the wait, so an earlier copy is one of the
    // orders the GPU may give, and reading the destination before the wait is a race on the GPU as well
    public static void CopyAsync<T>(ref T sharedDst, in T globalSrc)
    {
        Active(0);
        sharedDst = globalSrc;
    }

    public static void CopyAsyncCommitGroup() => Active(0);

    public static void CopyAsyncWaitGroup(int pendingGroups) => Active(pendingGroups);

    public static void CopyAsyncWaitAll() => Active(0);

    // The C heap, as the native CPU prelude reaches it (NativeMemory.Alloc calls malloc): null when the allocation
    // fails, as the device malloc returns, also for a negative size, which converts to a size the heap cannot give
    public static unsafe void* Malloc(int size)
    {
        Active(0);
        try
        {
            return NativeMemory.Alloc((nuint)(nint)size);
        }
        catch (OutOfMemoryException)
        {
            return null;
        }
    }

    public static unsafe void Free(void* ptr)
    {
        Active(0);
        NativeMemory.Free(ptr);
    }

    private static ulong ReadNanoseconds() =>
        (ulong)((UInt128)(ulong)Stopwatch.GetTimestamp() * NanosecondsPerSecond / (ulong)Stopwatch.Frequency);

    internal static T Active<T>(T value) => ManagedGpuContext.IsActive ? value : GpuOnlyException.Throw<T>();
}
