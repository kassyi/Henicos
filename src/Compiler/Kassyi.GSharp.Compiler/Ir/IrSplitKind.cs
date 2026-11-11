namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>Whether a call splits the state machine of the CPU paths, and at which kind of synchronization.</summary>
internal enum IrSplitKind
{
    /// <summary>The call does not split the state machine.</summary>
    None,

    /// <summary><c>Gpu.SyncThreads()</c>: every thread of the block waits for the others.</summary>
    BlockBarrier,

    /// <summary>
    /// <c>Gpu.SyncGrid()</c> and <c>Gpu.SyncGrid(body)</c>: every thread of the grid waits for the others.
    /// </summary>
    GridBarrier,

    /// <summary>
    /// A warp shuffle, match, vote or reduction, <c>Gpu.SyncWarp</c> or <c>Warp.ActiveMask()</c>: the lanes meet at the
    /// call (<see cref="IrSplit.Shuffle"/> says which one it is).
    /// </summary>
    WarpShuffle,

    /// <summary>A call of a device function that reaches one of the above, which suspends its caller.</summary>
    SuspendingCall
}
