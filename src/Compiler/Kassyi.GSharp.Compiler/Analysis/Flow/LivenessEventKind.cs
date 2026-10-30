namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>What a <see cref="LivenessEvent"/> does: a local read, written, pinned or changed, or a split.</summary>
internal enum LivenessEventKind
{
    Read,
    Write,
    Split,

    // A delegate that may run after any later split captures the local (forward, AddPinnedAcrossSplit)
    Pin,

    // The value of a tracked parameter may differ from its argument from here on (forward, FindChangedLiveAcrossSplit)
    Change
}
