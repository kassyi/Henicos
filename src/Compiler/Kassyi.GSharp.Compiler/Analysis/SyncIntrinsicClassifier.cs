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

    /// <summary><c>Warp.Shuffle*Sync</c>: reads the value of another lane named in the mask.</summary>
    WarpShuffle,

    /// <summary>A vote, reduction, match or WMMA operation that the lanes named in the mask (or the whole warp) take part in.</summary>
    WarpCollective,

    /// <summary><c>Gpu.SyncWarp(mask)</c>: the lanes named in the mask wait for each other.</summary>
    WarpBarrier,

    /// <summary><c>Gpu.ThreadFence*</c>: orders the memory accesses of the calling thread; no thread waits.</summary>
    MemoryFence,

    /// <summary><c>Gpu.MBarrier*</c>: an asynchronous barrier object in shared memory.</summary>
    AsyncBarrier
}

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
    Xor
}

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

/// <summary>
/// Classifies the synchronization intrinsics of <c>Kassyi.GSharp.Core</c> (<c>Gpu</c>, <c>Warp</c>, <c>Wmma</c>) by
/// symbol, so that the analyzers, the uniformity analysis and the lowering share one definition of which calls are
/// barriers, which split the state machine and which return thread-variant values.
/// </summary>
/// <remarks>
/// [WHY] A call is recognized by its symbol, never by how it is spelled: a <c>using</c> alias, <c>using static</c> or a
/// fully qualified name reaches the same method, and a user's own type named <c>Gpu</c> does not (#503). Members are
/// matched by their exact names; all overloads of a name share its classification (whether <c>Gpu.SyncGrid</c> has a
/// body is <see cref="GpuMacroClassifier"/>'s question). <c>SyncIntrinsicClassifierTests</c> compares the table with every
/// public static method of the three types, so a new intrinsic is classified when it is added.
/// </remarks>
internal static class SyncIntrinsicClassifier
{
    private static readonly SyncIntrinsicInfo s_variantValue =
        new(SyncIntrinsicKind.None, SyncScope.None, splitsStateMachine: false, resultIsThreadVariant: true);

    private static readonly SyncIntrinsicInfo s_warpCollective =
        new(SyncIntrinsicKind.WarpCollective, SyncScope.Warp, splitsStateMachine: false, resultIsThreadVariant: true);

    /// <summary>Classifies <paramref name="method"/>; any method that is not a member of the Core <c>Gpu</c>, <c>Warp</c> or <c>Wmma</c> type is <see langword="default"/>.</summary>
    public static SyncIntrinsicInfo Classify(IMethodSymbol method)
    {
        // Only the members of the top-level Core types are classified: not those of a nested type (Gpu.Hint), nor of a
        // user's own type of the same name
        if (method.ContainingType is not { ContainingType: null } type || !type.ContainingNamespace.IsCoreNamespace())
        {
            return default;
        }

        return type.Name switch
        {
            TypeConstants.GpuType => ClassifyGpuMember(method.Name),
            TypeConstants.WarpType => ClassifyWarpMember(method.Name),
            // Every lane of the warp takes part in each WMMA operation
            TypeConstants.WmmaType => s_warpCollective,
            _ => default
        };
    }

    /// <summary>Whether reading <paramref name="symbol"/> gives a value that may differ between the threads of a block (<c>ThreadIndex.X</c>, <c>.Y</c>, <c>.Z</c>).</summary>
    public static bool IsThreadVariantSource(ISymbol symbol) =>
        symbol is IPropertySymbol { ContainingType: { Name: TypeConstants.ThreadIndexType, ContainingType: null } type } &&
        type.ContainingNamespace.IsCoreNamespace();

    /// <summary>
    /// Whether reading <paramref name="symbol"/> gives a value that is the same in every thread of a block but may differ
    /// between the blocks of the grid (<c>BlockIndex.X</c>, <c>.Y</c>, <c>.Z</c>).
    /// </summary>
    public static bool IsGridVariantSource(ISymbol symbol) =>
        symbol is IPropertySymbol { ContainingType: { Name: TypeConstants.BlockIndexType, ContainingType: null } type } &&
        type.ContainingNamespace.IsCoreNamespace();

    private static SyncIntrinsicInfo ClassifyGpuMember(string name) => name switch
    {
        TypeConstants.SyncThreadsMethod =>
            new SyncIntrinsicInfo(SyncIntrinsicKind.BlockBarrier, SyncScope.Block, splitsStateMachine: true,
                resultIsThreadVariant: false),
        TypeConstants.GpuMacros.SyncGrid =>
            new SyncIntrinsicInfo(SyncIntrinsicKind.GridBarrier, SyncScope.Grid, splitsStateMachine: true,
                resultIsThreadVariant: false),
        "SyncWarp" =>
            new SyncIntrinsicInfo(SyncIntrinsicKind.WarpBarrier, SyncScope.Warp, splitsStateMachine: false,
                resultIsThreadVariant: false),
        "MatchAnySync" or "MatchAllSync" => s_warpCollective,
        "ThreadFence" or "ThreadFenceBlock" or "ThreadFenceSystem" =>
            new SyncIntrinsicInfo(SyncIntrinsicKind.MemoryFence, SyncScope.None, splitsStateMachine: false,
                resultIsThreadVariant: false),
        "MBarrierInit" or "MBarrierArrive" or "MBarrierWait" =>
            new SyncIntrinsicInfo(SyncIntrinsicKind.AsyncBarrier, SyncScope.None, splitsStateMachine: false,
                resultIsThreadVariant: false),
        "LaneId" or "WarpId" or "SmId" or "Clock" or "Clock64" => s_variantValue,
        "AtomicAdd" or "AtomicSub" or "AtomicExch" or "AtomicMin" or "AtomicMax" or "AtomicInc" or "AtomicDec"
            or "AtomicCAS" or "AtomicAnd" or "AtomicOr" or "AtomicXor" => s_variantValue,
        _ => default
    };

    private static SyncIntrinsicInfo ClassifyWarpMember(string name) => name switch
    {
        "ShuffleSync" => Shuffle(WarpShuffleKind.Sync),
        "ShuffleDownSync" => Shuffle(WarpShuffleKind.Down),
        "ShuffleUpSync" => Shuffle(WarpShuffleKind.Up),
        "ShuffleXorSync" => Shuffle(WarpShuffleKind.Xor),
        "BallotSync" or "AllSync" or "AnySync" or "ReduceAddSync" or "ReduceMinSync" or "ReduceMaxSync"
            or "ReduceAndSync" or "ReduceOrSync" or "ReduceXorSync" => s_warpCollective,
        "ActiveMask" => s_variantValue,
        _ => default
    };

    private static SyncIntrinsicInfo Shuffle(WarpShuffleKind shuffle) =>
        new(SyncIntrinsicKind.WarpShuffle, SyncScope.Warp, splitsStateMachine: true, resultIsThreadVariant: true,
            shuffle);
}
