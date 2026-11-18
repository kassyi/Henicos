namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>The reason a state machine suspends execution at the end of a state.</summary>
internal enum IrSuspendKind
{
    /// <summary>A block-wide barrier (<c>Gpu.SyncThreads()</c>).</summary>
    BlockBarrier,

    /// <summary>A grid-wide barrier (<c>Gpu.SyncGrid()</c>).</summary>
    GridBarrier,

    /// <summary>A warp-level publish before reading the collective result.</summary>
    WarpPublish,

    /// <summary>A warp reconvergence synchronization point (reserved for warp reconvergence).</summary>
    Reconverge
}
