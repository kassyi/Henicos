namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// The outcome of <see cref="CooperativeBlockSize.Choose"/>.
/// </summary>
/// <param name="BlockSize">
/// Selected block size in threads, or <c>0</c> if no candidate fits at least one active block per SM.
/// </param>
/// <param name="ActiveBlocksPerSm">
/// Active blocks per SM at <paramref name="BlockSize"/>, or <c>0</c> on failure.
/// </param>
/// <param name="MinCandidateBlockSize">Smallest candidate block size evaluated.</param>
/// <param name="FailureReason">Diagnostic reason when <see cref="IsSupported"/> is <see langword="false"/>.</param>
public readonly record struct CooperativeBlockSizeResult(
    int BlockSize,
    int ActiveBlocksPerSm,
    int MinCandidateBlockSize,
    string? FailureReason)
{
    /// <summary>Gets whether a candidate block size can reside on a streaming multiprocessor.</summary>
    public bool IsSupported => BlockSize > 0;
}
