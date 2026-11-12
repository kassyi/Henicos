namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>What kind of point splits the state machine (<see cref="SplitPoint"/>).</summary>
internal enum SplitKind
{
    /// <summary>A barrier, a warp shuffle or collective, or a call of a device function that suspends.</summary>
    Call,

    /// <summary>A <c>Gpu.SyncGrid(body)</c> whose body has a gap: a grid barrier where it is called.</summary>
    SyncGridCall,

    /// <summary>A gap between two statements of a <c>Gpu.SyncGrid(body)</c>, where the expansion syncs the grid.</summary>
    Gap
}
