namespace Kassyi.GSharp.Core;

/// <summary>
/// Selects how a grid barrier (<see cref="Gpu.SyncGrid(GridSyncMode)"/>) behaves after a thread of the launch failed.
/// </summary>
/// <remarks>
/// <para>
/// Both modes detect the failure in the same way: the launch throws the recorded exception, and no thread is left
/// waiting at the barrier. They differ only in what the other blocks may run after the failure.
/// </para>
/// <para>
/// The mode must be a compile-time constant (GSH0012). The CUDA build calls one barrier or the other; it does not
/// choose at run time. The native CPU build and the managed path treat both modes alike: they run the blocks of a
/// cooperative kernel in grid phases, where no block can arrive at a barrier while a failure is still unseen.
/// </para>
/// </remarks>
public enum GridSyncMode
{
    /// <summary>
    /// The default. A block looks at the failure signal of its launch before it arrives: a grid barrier reached after a
    /// thread of the launch failed does not complete, so no thread runs the code after it.
    /// </summary>
    Deterministic,

    /// <summary>
    /// A block arrives first and looks at the failure signal after its arrival, which saves the wait for that read on
    /// each barrier. After a thread of the launch failed, a barrier that a block of the failing launch completes lets
    /// the other blocks run one more interval, up to their next grid barrier, mbarrier wait or the end of the kernel,
    /// before they leave. The exception and its record are the same as with <see cref="Deterministic"/>; only the
    /// writes of that extra interval may differ from run to run. The code of that interval may read data that the
    /// failing block never wrote, so a loop whose count comes from such data can run for a long time before the block
    /// reaches the point where it leaves; use <see cref="Deterministic"/> when the code after the barrier depends on
    /// that.
    /// </summary>
    Fast
}
