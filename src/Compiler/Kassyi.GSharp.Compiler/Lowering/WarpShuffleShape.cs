namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// How the read of a warp collective is called: the Prelude function, the C# name for messages, and whether it takes
/// the value, the lane operand, the predicate target, the width, and the mask. <see cref="StackFunction"/> is the
/// function that runs the whole collective on the CPU stack models: (frame, site, "place", the arguments of the C#
/// call), the same name in the C++ prelude and in <c>ManagedGpuContext</c>.
/// </summary>
internal readonly record struct WarpShuffleShape(
    string ReadFunction,
    string StackFunction,
    string DisplayName,
    bool HasValue,
    bool HasLane,
    string? PredicateArgument,
    bool HasWidth,
    bool HasMask = true)
{
    /// <summary>Parameter position of the mask operand.</summary>
    public const int ShuffleMaskPosition = 0;

    /// <summary>Parameter position of the value or predicate operand.</summary>
    public const int ShuffleValuePosition = 1;

    /// <summary>Parameter position of the lane, delta, lane mask, or match predicate operand.</summary>
    public const int ShuffleLanePosition = 2;

    /// <summary>Parameter position of the shuffle width operand.</summary>
    public const int ShuffleWidthPosition = 3;

    /// <summary>Parameter name of the warp mask.</summary>
    public const string ShuffleMaskParameter = "mask";

    /// <summary>Parameter name of the value of a shuffle.</summary>
    public const string ShuffleValueParameter = "var";

    /// <summary>Parameter name of the value of a match collective.</summary>
    public const string MatchValueParameter = "value";

    /// <summary>Parameter name of the predicate of a vote collective.</summary>
    public const string VotePredicateParameter = "predicate";

    /// <summary>Parameter name of the value of a reduction collective.</summary>
    public const string ReduceValueParameter = "val";

    /// <summary>Parameter name of the shuffle width.</summary>
    public const string ShuffleWidthParameter = "width";

    private static readonly Dictionary<WarpShuffleKind, WarpShuffleShape> s_shapes = new()
    {
        [WarpShuffleKind.Sync] = Shuffle(
            GSharpInternalNames.Prelude.WarpShuffleReadSync,
            GSharpInternalNames.StackWarp.ShuffleSync,
            "Warp.ShuffleSync"),
        [WarpShuffleKind.Down] = Shuffle(
            GSharpInternalNames.Prelude.WarpShuffleReadDownSync,
            GSharpInternalNames.StackWarp.ShuffleDownSync,
            "Warp.ShuffleDownSync"),
        [WarpShuffleKind.Up] = Shuffle(
            GSharpInternalNames.Prelude.WarpShuffleReadUpSync,
            GSharpInternalNames.StackWarp.ShuffleUpSync,
            "Warp.ShuffleUpSync"),
        [WarpShuffleKind.Xor] = Shuffle(
            GSharpInternalNames.Prelude.WarpShuffleReadXorSync,
            GSharpInternalNames.StackWarp.ShuffleXorSync,
            "Warp.ShuffleXorSync"),
        [WarpShuffleKind.MatchAny] = Vote(
            GSharpInternalNames.Prelude.WarpMatchAnyRead,
            GSharpInternalNames.StackWarp.MatchAnySync,
            "Gpu.MatchAnySync"),
        [WarpShuffleKind.MatchAll] = new(
            GSharpInternalNames.Prelude.WarpMatchAllRead,
            GSharpInternalNames.StackWarp.MatchAllSync,
            "Gpu.MatchAllSync",
            HasValue: true,
            HasLane: false,
            PredicateArgument: "pred",
            HasWidth: false),
        // The votes and reductions read the mask and the published predicate or value of every lane (#845)
        [WarpShuffleKind.Ballot] = Vote(
            GSharpInternalNames.Prelude.WarpBallotRead,
            GSharpInternalNames.StackWarp.BallotSync,
            "Warp.BallotSync"),
        [WarpShuffleKind.All] = Vote(
            GSharpInternalNames.Prelude.WarpAllRead,
            GSharpInternalNames.StackWarp.AllSync,
            "Warp.AllSync"),
        [WarpShuffleKind.Any] = Vote(
            GSharpInternalNames.Prelude.WarpAnyRead,
            GSharpInternalNames.StackWarp.AnySync,
            "Warp.AnySync"),
        [WarpShuffleKind.ReduceAdd] = Vote(
            GSharpInternalNames.Prelude.WarpReduceAddRead,
            GSharpInternalNames.StackWarp.ReduceAddSync,
            "Warp.ReduceAddSync"),
        [WarpShuffleKind.ReduceMin] = Vote(
            GSharpInternalNames.Prelude.WarpReduceMinRead,
            GSharpInternalNames.StackWarp.ReduceMinSync,
            "Warp.ReduceMinSync"),
        [WarpShuffleKind.ReduceMax] = Vote(
            GSharpInternalNames.Prelude.WarpReduceMaxRead,
            GSharpInternalNames.StackWarp.ReduceMaxSync,
            "Warp.ReduceMaxSync"),
        [WarpShuffleKind.ReduceAnd] = Vote(
            GSharpInternalNames.Prelude.WarpReduceAndRead,
            GSharpInternalNames.StackWarp.ReduceAndSync,
            "Warp.ReduceAndSync"),
        [WarpShuffleKind.ReduceOr] = Vote(
            GSharpInternalNames.Prelude.WarpReduceOrRead,
            GSharpInternalNames.StackWarp.ReduceOrSync,
            "Warp.ReduceOrSync"),
        [WarpShuffleKind.ReduceXor] = Vote(
            GSharpInternalNames.Prelude.WarpReduceXorRead,
            GSharpInternalNames.StackWarp.ReduceXorSync,
            "Warp.ReduceXorSync"),
        // The lanes of the mask meet without a value
        [WarpShuffleKind.SyncWarp] = new(
            GSharpInternalNames.Prelude.WarpSyncRead,
            GSharpInternalNames.StackWarp.SyncWarp,
            "Gpu.SyncWarp",
            HasValue: false,
            HasLane: false,
            PredicateArgument: null,
            HasWidth: false),
        // The lanes that stopped at the same call in the same round: read(<root>, <frame>, <site>, "<place>")
        [WarpShuffleKind.ActiveMask] = new(
            GSharpInternalNames.Prelude.WarpActiveMaskRead,
            GSharpInternalNames.StackWarp.ActiveMask,
            "Warp.ActiveMask",
            HasValue: false,
            HasLane: false,
            PredicateArgument: null,
            HasWidth: false,
            HasMask: false),
    };

    // A shuffle: read(<root>, <frame>, <site>, mask, val, <lane operand>, width, "<place>")
    private static WarpShuffleShape Shuffle(string readFunction, string stackFunction, string displayName) =>
        new(readFunction, stackFunction, displayName, HasValue: true, HasLane: true,
            PredicateArgument: null, HasWidth: true);

    // A vote, reduction or match: read(<root>, <frame>, <site>, mask, <predicate or value>, "<place>")
    private static WarpShuffleShape Vote(string readFunction, string stackFunction, string displayName) =>
        new(readFunction, stackFunction, displayName, HasValue: true, HasLane: false,
            PredicateArgument: null, HasWidth: false);

    /// <summary>Returns the shape for <paramref name="kind"/>.</summary>
    public static WarpShuffleShape ForKind(WarpShuffleKind kind) =>
        s_shapes.TryGetValue(kind, out var shape)
            ? shape
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, $"No shuffle shape for kind {kind}");
}
