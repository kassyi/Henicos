// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Kernels that exercise <see cref="SharedMemory{T}"/> declarations inside <c>[GpuDeviceFunction]</c> helpers across
/// multiple calls, caller/callee pairs, size growth, and recursion.
/// </summary>
public static partial class DeviceDynamicSharedMemoryKernels
{
    // The helper reaches a barrier (lowered on the CPU dialects); its buffer must not overlap the caller's
    [GpuDeviceFunction]
    public static int SuspendingHelper(int n, int val)
    {
        var inner = new SharedMemory<int>(n);
        inner[ThreadIndex.X] = val * 2;
        Gpu.SyncThreads();
        return inner[ThreadIndex.X];
    }

    [GpuKernel]
    public static void SuspendingCallerKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        var outer = new SharedMemory<int>(n);
        outer[tid] = tid + 100;
        Gpu.SyncThreads();
        int helperResult = SuspendingHelper(n, tid);
        Gpu.SyncThreads();
        output[tid] = outer[tid] + helperResult;
    }

    // One helper reached from two kernels; one declares no buffer, the other declares one and calls it after a barrier
    [GpuDeviceFunction]
    public static int TwoKernelHelper(int n, int val)
    {
        var inner = new SharedMemory<int>(n);
        inner[ThreadIndex.X] = val * 2;
        Gpu.SyncThreads();
        return inner[ThreadIndex.X];
    }

    [GpuKernel]
    public static void HelperOnlyKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        output[tid] = TwoKernelHelper(n, tid);
    }

    [GpuKernel]
    public static void HelperAndCallerKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        var outer = new SharedMemory<int>(n);
        outer[tid] = tid + 100;
        Gpu.SyncThreads();
        int helperResult = TwoKernelHelper(n, tid);
        Gpu.SyncThreads();
        output[tid] = outer[tid] + helperResult;
    }

    // The helper never reaches a barrier (not lowered on the CPU dialects)
    [GpuDeviceFunction]
    public static int PlainHelper(int n, int val)
    {
        var inner = new SharedMemory<int>(n);
        inner[ThreadIndex.X] = val * 2;
        return inner[ThreadIndex.X];
    }

    [GpuKernel]
    public static void PlainCallerKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        var outer = new SharedMemory<int>(n);
        outer[tid] = tid + 100;
        Gpu.SyncThreads();
        int helperResult = PlainHelper(n, tid);
        Gpu.SyncThreads();
        output[tid] = outer[tid] + helperResult;
    }

    // The same dynamic declaration reached by two calls of a suspending helper (lowered on the CPU dialects)
    [GpuDeviceFunction]
    public static int SuspendingPersistHelper(int n, int val, bool write)
    {
        var s = new SharedMemory<int>(n);
        if (write)
        {
            s[ThreadIndex.X] = val;
        }

        Gpu.SyncThreads();
        return s[(ThreadIndex.X + 1) % n];
    }

    [GpuKernel]
    public static void SuspendingPersistKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        SuspendingPersistHelper(n, tid + 1, true);
        Gpu.SyncThreads();
        output[tid] = SuspendingPersistHelper(n, 0, false);
    }

    // The same dynamic declaration reached by two calls of a non-suspending helper
    [GpuDeviceFunction]
    public static int PlainPersistHelper(int n, int val, bool write)
    {
        var s = new SharedMemory<int>(n);
        if (write)
        {
            s[ThreadIndex.X] = val;
        }

        return s[(ThreadIndex.X + 1) % n];
    }

    [GpuKernel]
    public static void PlainPersistKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        PlainPersistHelper(n, tid + 1, true);
        Gpu.SyncThreads();
        output[tid] = PlainPersistHelper(n, 0, false);
    }

    // Static-size shared memory in a non-suspending helper, read back by a second call
    [GpuDeviceFunction]
    public static int StaticPersistHelper(int val, bool write)
    {
        var s = new SharedMemory<int>(32);
        if (write)
        {
            s[ThreadIndex.X] = val;
        }

        return s[(ThreadIndex.X + 1) % 32];
    }

    [GpuKernel]
    public static void StaticPersistKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        StaticPersistHelper(tid + 1, true);
        Gpu.SyncThreads();
        output[tid] = StaticPersistHelper(0, false);
    }

    // The helper's declaration is first reached with a small count, then the caller declares its own buffer,
    // then the helper is reached again with a larger count; earlier elements of the grown buffer must also survive
    [GpuDeviceFunction]
    public static int GrowHelper(int count, int writeStart, int val)
    {
        int tid = ThreadIndex.X;
        var s = new SharedMemory<int>(count);
        if (tid >= writeStart && tid < count)
        {
            s[tid] = val * 2;
        }

        Gpu.SyncThreads();
        int r = tid < count ? s[tid] : 0;
        Gpu.SyncThreads();
        return r;
    }

    [GpuKernel]
    public static void GrowAfterCallerKernel(int[] output, int small, int large)
    {
        int tid = ThreadIndex.X;
        int h1 = GrowHelper(small, 0, tid);
        var outer = new SharedMemory<int>(large);
        outer[tid] = tid + 100;
        Gpu.SyncThreads();
        int h2 = GrowHelper(large, small, tid);
        Gpu.SyncThreads();
        output[tid] = outer[tid] + h2 + h1 * 0;
    }

    // Recursive non-suspending helper (each recursion level reaches the same declaration and must share one buffer)
    [GpuDeviceFunction]
    public static int Rec(int n, int depth)
    {
        int tid = ThreadIndex.X;
        var s = new SharedMemory<int>(n);
        if (depth == 0)
        {
            s[tid] = tid + 1;
            return s[tid];
        }

        Rec(n, depth - 1);
        s[tid] += 10;
        return s[tid];
    }

    [GpuKernel]
    public static void RecursiveKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        var outer = new SharedMemory<int>(n);
        outer[tid] = tid + 100;
        int r = Rec(n, 2);
        output[tid] = outer[tid] + r;
    }

    // A dynamic declaration in an inner block followed by a second one in the enclosing block; the second round
    // reads back the inner buffer after the outer one was written, so the two must not overlap
    [GpuKernel]
    public static void NestedScopeKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        int inner = 0;
        int outer = 0;
        for (int round = 0; round < 2; round++)
        {
            if (n > 0)
            {
                var a = new SharedMemory<int>(n);
                if (round == 0)
                {
                    a[tid] = tid + 1;
                }
                else
                {
                    inner = a[tid];
                }
            }

            var b = new SharedMemory<int>(n);
            b[tid] = 1000 + tid;
            outer = b[tid];
        }

        output[tid] = inner + outer;
    }

    // The helper's declaration is first reached with a count of 0, then with a larger count while it is still the
    // last region; growing it in place must not count as growing after a later declaration
    [GpuKernel]
    public static void ZeroThenGrowKernel(int[] output, int small, int large)
    {
        int tid = ThreadIndex.X;
        var outer = new SharedMemory<int>(small);
        if (tid < small)
        {
            outer[tid] = tid + 100;
        }

        Gpu.SyncThreads();
        int h0 = GrowHelper(0, 0, tid);
        int h1 = GrowHelper(large, 0, tid);
        Gpu.SyncThreads();
        output[tid] = (tid < small ? outer[tid] : 0) + h1 + h0;
    }

    // A device function without a declaration of its own between the kernel and the helper that declares one
    [GpuDeviceFunction]
    public static int Middle(int n, int val) => PlainHelper(n, val);

    [GpuKernel]
    public static void MiddleCallerKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        var outer = new SharedMemory<int>(n);
        outer[tid] = tid + 100;
        Gpu.SyncThreads();
        int helperResult = Middle(n, tid);
        Gpu.SyncThreads();
        output[tid] = outer[tid] + helperResult;
    }

    // A helper without parameters, so the table is its only argument
    [GpuDeviceFunction]
    public static int NoParameterHelper()
    {
        int tid = ThreadIndex.X;
        var s = new SharedMemory<int>(BlockDimension.X);
        s[tid] = tid * 3;
        return s[tid];
    }

    [GpuKernel]
    public static void NoParameterCallerKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        output[tid] = NoParameterHelper();
    }

    // Helpers called from a local function and from a lambda of the kernel, which capture the table by reference
    [GpuKernel]
    public static void NestedFunctionCallerKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        var outer = new SharedMemory<int>(n);
        outer[tid] = tid + 100;
        Gpu.SyncThreads();

        int CallPlainHelper(int val) => PlainHelper(n, val);
        var callNoParameterHelper = () => NoParameterHelper();

        int plain = CallPlainHelper(tid);
        int noParameter = callNoParameterHelper();
        Gpu.SyncThreads();
        output[tid] = outer[tid] + plain + noParameter;
    }

    // A negative count after a 128-byte region. Its size, (unsigned long long)(long long)(negative) * sizeof(int),
    // plus the offset 128 wraps past 2^64; the placement must fail instead of moving the cursor back, which would let the
    // third declaration overlap the first
    [GpuKernel]
    public static void NegativeCountKernel(int[] output, int n, int negative)
    {
        int tid = ThreadIndex.X;
        var first = new SharedMemory<int>(n);
        first[tid] = tid;
        var second = new SharedMemory<int>(negative);
        var third = new SharedMemory<int>(n);
        third[tid] = 1000 + tid;
        Gpu.SyncThreads();
        output[tid] = first[tid];
    }
}
