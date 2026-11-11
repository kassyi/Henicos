using System.Collections.Generic;
using System.Diagnostics;
using Kassyi.GSharp.Core;

namespace RemovedCallDiagnosticsCase;

// Each removed call (Trace, TraceSync) has a twin that C# keeps (Keep, KeepSync): only the twins are diagnosed
public static partial class RemovedCallKernels
{
    [GpuDeviceFunction][Conditional("NEVER_DEFINED")] public static void Trace(int value) { }
    [GpuDeviceFunction] public static void Keep(int value) { }
    [GpuDeviceFunction][Conditional("NEVER_DEFINED")] public static void TraceSync(int[] d) { Gpu.SyncThreads(); d[0] = 5; }
    [GpuDeviceFunction] public static void KeepSync(int[] d) { Gpu.SyncThreads(); d[0] = 5; }
    [GpuDeviceFunction] public static int BumpAfterBarrier(int[] d, int i) { Gpu.SyncThreads(); d[i] += 100; return d[i]; }

    [GpuDeviceFunction]
    public static IEnumerable<int> RemovedValues(int[] d, int i)
    {
        Trace(BumpAfterBarrier(d, i));
        yield return d[i];
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> KeptValues(int[] d, int i)
    {
        Keep(BumpAfterBarrier(d, i));
        yield return d[i];
    }

    [GpuKernel]
    public static void IteratorKernel(int[] d)
    {
        int i = ThreadIndex.X;
        int s = 0;
        foreach (var v in RemovedValues(d, i)) s += v;
        foreach (var v in KeptValues(d, i)) s += v;
        d[i] = s;
    }

    [GpuKernel]
    public static void RemovedBranchBarrierKernel(int[] d)
    {
        int i = ThreadIndex.X;
        if (i == 0) TraceSync(d);
        d[i] += 1;
    }

    [GpuKernel]
    public static void KeptBranchBarrierKernel(int[] d)
    {
        int i = ThreadIndex.X;
        if (i == 0) KeepSync(d);
        d[i] += 1;
    }

    [GpuKernel]
    public static void RemovedConditionalArgumentKernel(int[] d)
    {
        int i = ThreadIndex.X;
        Trace(i > 0 && BumpAfterBarrier(d, i) > 0 ? 1 : 0);
        d[i] += 1;
    }

    [GpuKernel]
    public static void KeptConditionalArgumentKernel(int[] d)
    {
        int i = ThreadIndex.X;
        Keep(i > 0 && BumpAfterBarrier(d, i) > 0 ? 1 : 0);
        d[i] += 1;
    }

    // A suspending call in a local function is GSH0062, unless it is the argument of a removed call
    [GpuKernel]
    public static void RemovedLocalFunctionKernel(int[] d)
    {
        int i = ThreadIndex.X;
        void Log(int v) => Trace(BumpAfterBarrier(d, v));
        Log(i);
        d[i] += 1;
    }

    [GpuKernel]
    public static void KeptLocalFunctionKernel(int[] d)
    {
        int i = ThreadIndex.X;
        void Log(int v) => Keep(BumpAfterBarrier(d, v));
        Log(i);
        d[i] += 1;
    }

    // A call that reaches a dynamic SharedMemory<T> under a thread-dependent branch is GSH0077, unless C# removes it
    [GpuDeviceFunction][Conditional("NEVER_DEFINED")] public static void TraceShared(int n) { var s = new SharedMemory<int>(n); s[0] = 1; }
    [GpuDeviceFunction] public static void KeepShared(int n) { var s = new SharedMemory<int>(n); s[0] = 1; }

    [GpuKernel]
    public static void RemovedSharedMemoryKernel(int[] d, int n)
    {
        int i = ThreadIndex.X;
        if (i == 0) TraceShared(n);
        d[i] += 1;
    }

    [GpuKernel]
    public static void KeptSharedMemoryKernel(int[] d, int n)
    {
        int i = ThreadIndex.X;
        if (i == 0) KeepShared(n);
        d[i] += 1;
    }
}
