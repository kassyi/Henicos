namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>Which warp shuffle a call is, which selects the Prelude function that reads its result after a resume.</summary>
internal enum WarpShuffleKind
{
    /// <summary>Not a warp shuffle.</summary>
    None,

    /// <summary><c>Warp.ShuffleSync</c>.</summary>
    Sync,

    /// <summary><c>Warp.ShuffleDownSync</c>.</summary>
    Down,

    /// <summary><c>Warp.ShuffleUpSync</c>.</summary>
    Up,

    /// <summary><c>Warp.ShuffleXorSync</c>.</summary>
    Xor,

    /// <summary><c>Gpu.MatchAnySync</c>.</summary>
    MatchAny,

    /// <summary><c>Gpu.MatchAllSync</c>.</summary>
    MatchAll,

    /// <summary><c>Warp.BallotSync</c>.</summary>
    Ballot,

    /// <summary><c>Warp.AllSync</c>.</summary>
    All,

    /// <summary><c>Warp.AnySync</c>.</summary>
    Any,

    /// <summary><c>Warp.ReduceAddSync</c>.</summary>
    ReduceAdd,

    /// <summary><c>Warp.ReduceMinSync</c>.</summary>
    ReduceMin,

    /// <summary><c>Warp.ReduceMaxSync</c>.</summary>
    ReduceMax,

    /// <summary><c>Warp.ReduceAndSync</c>.</summary>
    ReduceAnd,

    /// <summary><c>Warp.ReduceOrSync</c>.</summary>
    ReduceOr,

    /// <summary><c>Warp.ReduceXorSync</c>.</summary>
    ReduceXor,

    /// <summary><c>Gpu.SyncWarp</c>: the lanes of the mask meet, without a value.</summary>
    SyncWarp,

    /// <summary>
    /// <c>Warp.ActiveMask()</c>: the lanes of the warp that reach the call together, which the CPU paths find among the
    /// lanes that stop at the same call in the same round.
    /// </summary>
    ActiveMask
}
