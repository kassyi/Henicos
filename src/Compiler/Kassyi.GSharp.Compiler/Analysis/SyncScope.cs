namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>The threads that must all reach a synchronization intrinsic, or that it may make differ.</summary>
internal enum SyncScope
{
    /// <summary>No set of threads has to reach the intrinsic together.</summary>
    None,

    /// <summary>The lanes of a warp named in the mask.</summary>
    Warp,

    /// <summary>Every thread of the block.</summary>
    Block,

    /// <summary>Every thread of the grid.</summary>
    Grid
}
