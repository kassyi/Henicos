using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// Ref locals in kernels and device functions that the CPU dialects split into states. A ref local that some state after
// a split reads is GSH0039; one whose uses all lie before the split stays a local of its state (#1640)
[SkipSnapshotConsistency("Diagnostics case")]
public static partial class RefLocalAcrossSplitKernels
{
    [GpuDeviceFunction]
    public static int Suspend(int v)
    {
        Gpu.SyncThreads();
        return v;
    }

    // Live across Gpu.SyncThreads()
    [GpuKernel]
    public static void HeldAcrossSyncThreads(int[] r)
    {
        int w = r[0];
        ref int alias = ref w;
        Gpu.SyncThreads();
        alias += 1;
        r[0] = w;
    }

    // Live across a call to a device function that reaches a barrier
    [GpuKernel]
    public static void HeldAcrossSuspendingCall(int[] r)
    {
        int w = r[0];
        ref int alias = ref w;
        int t = Suspend(1);
        alias += t;
        r[0] = w;
    }

    // Live across a warp shuffle
    [GpuKernel]
    public static void HeldAcrossWarpShuffle(int[] r)
    {
        int w = r[ThreadIndex.X];
        ref int slot = ref w;
        int t = Warp.ShuffleSync(0xFFFFFFFFu, w, 0);
        slot += t;
        r[ThreadIndex.X] = w;
    }

    // Live across a barrier in a device function that may suspend
    [GpuDeviceFunction]
    public static int HeldInDeviceFunction(int v)
    {
        int w = v;
        ref int alias = ref w;
        Gpu.SyncThreads();
        alias += 1;
        return w;
    }

    [GpuKernel]
    public static void CallsHeldInDeviceFunction(int[] r)
    {
        r[0] = HeldInDeviceFunction(r[0]);
    }

    // Used only before the barrier
    [GpuKernel]
    public static void UsedBeforeSyncThreads(int[] r)
    {
        int w = r[ThreadIndex.X];
        ref int alias = ref w;
        alias += 1;
        Gpu.SyncThreads();
        r[ThreadIndex.X] = w;
    }

    // Declared again in every iteration, used only before the barrier of the iteration
    [GpuKernel]
    public static void UsedBeforeSyncThreadsInLoop(int[] r)
    {
        int w = r[ThreadIndex.X];
        for (int i = 0; i < 2; i++)
        {
            ref int alias = ref w;
            alias += 1;
            Gpu.SyncThreads();
        }

        r[ThreadIndex.X] = w;
    }

    // Declared in a branch that ends before the barrier
    [GpuKernel]
    public static void UsedInBranchBeforeSyncThreads(int[] r)
    {
        int w = r[ThreadIndex.X];
        if ((ThreadIndex.X & 1) == 0)
        {
            ref int alias = ref w;
            alias += 1;
        }

        Gpu.SyncThreads();
        r[ThreadIndex.X] = w;
    }

    // Used only before the barrier of a device function that may suspend
    [GpuDeviceFunction]
    public static int UsedBeforeSyncThreadsInDeviceFunction(int v)
    {
        int w = v;
        ref int alias = ref w;
        alias += 1;
        Gpu.SyncThreads();
        return w;
    }

    [GpuKernel]
    public static void CallsUsedBeforeSyncThreadsInDeviceFunction(int[] r)
    {
        r[ThreadIndex.X] = UsedBeforeSyncThreadsInDeviceFunction(r[ThreadIndex.X]);
    }

    // Rebound after the barrier: not live across it, but named in the next state
    [GpuKernel]
    public static void ReboundAfterSyncThreads(int[] r)
    {
        int x = r[0];
        int y = r[1];
        ref int alias = ref x;
        alias += 1;
        Gpu.SyncThreads();
        alias = ref y;
        alias += 10;
        r[0] = x;
        r[1] = y;
    }

    public static void RunUsedBeforeSyncThreads(int[] r, uint blockDimX) =>
        LaunchUsedBeforeSyncThreads(r, blockDimX: blockDimX);

    public static void RunUsedBeforeSyncThreadsInLoop(int[] r, uint blockDimX) =>
        LaunchUsedBeforeSyncThreadsInLoop(r, blockDimX: blockDimX);

    public static void RunUsedInBranchBeforeSyncThreads(int[] r, uint blockDimX) =>
        LaunchUsedInBranchBeforeSyncThreads(r, blockDimX: blockDimX);

    public static void RunCallsUsedBeforeSyncThreadsInDeviceFunction(int[] r, uint blockDimX) =>
        LaunchCallsUsedBeforeSyncThreadsInDeviceFunction(r, blockDimX: blockDimX);

    public static void RunHeldAcrossSyncThreads(int[] r, uint blockDimX) =>
        LaunchHeldAcrossSyncThreads(r, blockDimX: blockDimX);
}
