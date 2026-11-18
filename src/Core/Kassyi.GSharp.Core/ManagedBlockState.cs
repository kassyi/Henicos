namespace Kassyi.GSharp.Core;

// The per-launch state shared by all GPU threads of a grid or block on the managed CPU path: extracted from
// [ThreadStatic] fields on ManagedGpuContext so the launching thread can share one instance with worker OS threads.
internal sealed class ManagedBlockState
{
#pragma warning disable SA1401 // Data-only internal block state; array fields are passed by ref to Array.Resize
    // One region per static shared memory declaration per block (a constant count, or a declaration in code that the
    // compiler does not rewrite). A later declaration never moves an earlier region: threads keep the span they were
    // handed, so moving a region would leave them writing to a copy nobody else reads. Only the same declaration reached
    // with a larger count moves its own region (see GrowRegion), which a constant count never does.
    // The state is kept between grids; only the region counts are reset.
    internal ManagedGpuContext.SharedRegion?[]?[]? PerBlockRegions;

    internal int[]? PerBlockRegionCount;

    // The dynamic shared memory of each block (#544): one array of the launch's sharedMemBytes, in which the dynamic
    // declarations get their regions with the arithmetic of the CUDA placement (__gsharp_dsmem_place in Prelude.cu) and
    // of the native CPU arena. When the blocks run in grid phases (a cooperative kernel), every block keeps its array for
    // the grid, so a span stays valid across the grid syncs. Otherwise each block runs to its end before the next one
    // starts, and the blocks use one array in turn, as the native arena of an OpenMP thread does.
    internal ManagedGpuContext.DynamicSharedMemory?[]? PerBlockDynamic;

    internal bool BlocksRunInPhases;

    // The block that owns the one array of blocks that do not run in phases, or -1
    internal int DynamicOwner;

    internal uint SharedMemBytes;

    // The last two values each thread of the block published, which slot it writes next, whether it waits at a warp
    // collective and whether it has exited
    internal ManagedGpuContext.WarpRecord[]? WarpRecords;

    internal int[]? WarpRecordNext;

    internal bool[]? WarpWaiting;

    internal bool[]? WarpExited;

    // The launcher's round of the block
    internal int WarpRound;

    // The thread-dependent branches and loops of methods that reach Warp.ActiveMask() that each thread of the block is
    // inside, innermost last (#844): WarpRegionCapacity entries per thread; and whether it waits where the innermost
    // one ends, since which round and where. Allocated by the first kernel that enters one
    internal ManagedGpuContext.WarpRegion[]? WarpRegions;

    internal int[]? WarpRegionDepth;

    internal bool[]? WarpReconverging;

    internal int[]? WarpReconvergeRound;

    internal string?[]? WarpReconvergeLocation;

    // The round in which the waiting lanes of each warp of the block were last decided, and whether each thread goes on
    internal int[]? WarpDecidedRound;

    internal bool[]? WarpMayPass;
#pragma warning restore SA1401
}
