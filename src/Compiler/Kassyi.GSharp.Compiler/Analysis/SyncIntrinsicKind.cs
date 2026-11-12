namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>What a synchronization intrinsic of <c>Kassyi.GSharp.Core</c> waits for or exchanges.</summary>
internal enum SyncIntrinsicKind
{
    /// <summary>Not a synchronization intrinsic (it may still return a thread-variant value).</summary>
    None,

    /// <summary><c>Gpu.SyncThreads()</c>: every thread of the block waits for the others.</summary>
    BlockBarrier,

    /// <summary><c>Gpu.SyncGrid()</c> and <c>Gpu.SyncGrid(body)</c>: every thread of the grid waits for the others.</summary>
    GridBarrier,

    /// <summary>
    /// <c>Warp.Shuffle*Sync</c>, <c>Gpu.Match*Sync</c>, the votes and reductions of <c>Warp</c>, and
    /// <c>Gpu.SyncWarp(mask)</c>: the lanes named in the mask meet at the call and read each other's values (none for
    /// <c>Gpu.SyncWarp</c>), which the CPU paths exchange across a state boundary.
    /// </summary>
    WarpShuffle,

    /// <summary>A WMMA operation that every lane of the warp takes part in.</summary>
    WarpCollective,

    /// <summary><c>Gpu.ThreadFence*</c>: orders the memory accesses of the calling thread; no thread waits.</summary>
    MemoryFence,

    /// <summary><c>Gpu.MBarrier*</c>: an asynchronous barrier object in shared memory.</summary>
    AsyncBarrier
}
