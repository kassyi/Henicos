namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>How the analyzers and the lowering treat a call to an intrinsic of <c>Kassyi.GSharp.Core</c>.</summary>
/// <param name="kind">What the intrinsic synchronizes.</param>
/// <param name="scope">The threads that must all reach it.</param>
/// <param name="splitsStateMachine">
/// Whether the CPU dialects and the managed path split the state machine at the call, so that every thread of the block
/// runs up to it before any thread goes on.
/// </param>
/// <param name="resultIsThreadVariant">Whether a value it returns may differ between the threads of a block.</param>
/// <param name="shuffle">Which warp shuffle it is.</param>
internal readonly struct SyncIntrinsicInfo(
    SyncIntrinsicKind kind,
    SyncScope scope,
    bool splitsStateMachine,
    bool resultIsThreadVariant,
    WarpShuffleKind shuffle = WarpShuffleKind.None)
{
    /// <summary>What the intrinsic synchronizes.</summary>
    public SyncIntrinsicKind Kind { get; } = kind;

    /// <summary>The threads that must all reach it.</summary>
    public SyncScope Scope { get; } = scope;

    /// <summary>Whether the CPU dialects and the managed path split the state machine at the call.</summary>
    public bool SplitsStateMachine { get; } = splitsStateMachine;

    /// <summary>Whether a value it returns may differ between the threads of a block.</summary>
    public bool ResultIsThreadVariant { get; } = resultIsThreadVariant;

    /// <summary>Which warp shuffle it is (<see cref="WarpShuffleKind.None"/> for any other intrinsic).</summary>
    public WarpShuffleKind Shuffle { get; } = shuffle;
}
