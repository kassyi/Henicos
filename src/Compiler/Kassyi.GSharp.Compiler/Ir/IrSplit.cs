using Kassyi.GSharp.Compiler.Analysis;

namespace Kassyi.GSharp.Compiler.Ir;

/// <summary>Whether and how a call splits the state machine of the CPU paths.</summary>
/// <param name="Kind">The kind of split.</param>
/// <param name="Shuffle">Which warp operation a <see cref="IrSplitKind.WarpShuffle"/> is; otherwise
/// <see cref="WarpShuffleKind.None"/>.</param>
internal readonly record struct IrSplit(IrSplitKind Kind, WarpShuffleKind Shuffle)
{
    /// <summary>A call that does not split.</summary>
    public static IrSplit None => new(IrSplitKind.None, WarpShuffleKind.None);

    /// <summary>A block barrier.</summary>
    public static IrSplit BlockBarrier => new(IrSplitKind.BlockBarrier, WarpShuffleKind.None);

    /// <summary>A grid barrier.</summary>
    public static IrSplit GridBarrier => new(IrSplitKind.GridBarrier, WarpShuffleKind.None);

    /// <summary>A call of a device function that suspends its caller.</summary>
    public static IrSplit SuspendingCall => new(IrSplitKind.SuspendingCall, WarpShuffleKind.None);

    /// <summary>Whether the call splits the state machine.</summary>
    public bool IsSplit => Kind != IrSplitKind.None;

    /// <summary>The warp operation <paramref name="shuffle"/>.</summary>
    public static IrSplit WarpShuffle(WarpShuffleKind shuffle) => shuffle == WarpShuffleKind.None
        ? throw new ArgumentOutOfRangeException(nameof(shuffle), "A warp shuffle split names its operation.")
        : new IrSplit(IrSplitKind.WarpShuffle, shuffle);
}
