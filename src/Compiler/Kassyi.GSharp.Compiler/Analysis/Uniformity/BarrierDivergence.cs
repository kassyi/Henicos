namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Whether only some of the threads that must reach a barrier reach it, and what the others do instead.
/// </summary>
internal enum BarrierDivergence
{
    /// <summary>Every thread that must reach the barrier reaches it.</summary>
    None,

    /// <summary>
    /// The threads that skip the barrier leave the method without reaching another point where the state machine
    /// splits.
    /// </summary>
    ExitOnly,

    /// <summary>
    /// The threads that skip the barrier reach another point where the state machine splits: a later barrier, or the
    /// barrier itself in the next iteration of a loop.
    /// </summary>
    Condition
}
