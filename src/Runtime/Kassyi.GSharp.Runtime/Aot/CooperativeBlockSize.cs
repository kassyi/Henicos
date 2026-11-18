namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Pure selector that picks the largest candidate block size for a cooperative kernel launch that fits at least one
/// active block on a streaming multiprocessor.
/// </summary>
public static class CooperativeBlockSize
{
    /// <summary>
    /// Default descending candidate block sizes for cooperative mega-kernels requiring warp-aligned thread counts
    /// of at least 256 threads.
    /// </summary>
    public static ReadOnlySpan<int> DefaultCandidates => [1024, 768, 512, 256];

    /// <summary>
    /// Selects the largest candidate in <paramref name="candidates"/> for which
    /// <paramref name="activeBlocksPerSm"/> returns at least <c>1</c>.
    /// </summary>
    /// <param name="candidates">Non-empty candidate block sizes (each strictly positive).</param>
    /// <param name="activeBlocksPerSm">Occupancy callback returning active blocks per SM for a block size.</param>
    /// <param name="kernelInfo">
    /// Optional resource and SM limit metadata used to format failure diagnostics.
    /// </param>
    /// <returns>The selection result, including a failure diagnostic if no candidate fits.</returns>
    /// <exception cref="ArgumentException"><paramref name="candidates"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="activeBlocksPerSm"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Any candidate block size is not positive.</exception>
    public static CooperativeBlockSizeResult Choose(
        ReadOnlySpan<int> candidates,
        Func<int, int> activeBlocksPerSm,
        in CooperativeKernelResources kernelInfo = default)
    {
        ArgumentNullException.ThrowIfNull(activeBlocksPerSm);
        if (candidates.IsEmpty)
        {
            throw new ArgumentException("At least one candidate block size is required.", nameof(candidates));
        }

        var minCandidate = int.MaxValue;
        var bestBlockSize = 0;
        var bestActiveBlocks = 0;

        for (var i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidate, nameof(candidates));
            if (candidate < minCandidate)
            {
                minCandidate = candidate;
            }

            var activeBlocks = activeBlocksPerSm(candidate);
            if (activeBlocks >= 1 && (bestBlockSize == 0 || candidate > bestBlockSize))
            {
                bestBlockSize = candidate;
                bestActiveBlocks = activeBlocks;
            }
        }

        if (bestBlockSize > 0)
        {
            return new CooperativeBlockSizeResult(bestBlockSize, bestActiveBlocks, minCandidate, FailureReason: null);
        }

        return new CooperativeBlockSizeResult(
            BlockSize: 0,
            ActiveBlocksPerSm: 0,
            MinCandidateBlockSize: minCandidate,
            FailureReason: FormatFailureReason(in kernelInfo, minCandidate));
    }

    internal static string FormatFailureReason(in CooperativeKernelResources kernelInfo, int minCandidate)
    {
        var kernelName = string.IsNullOrWhiteSpace(kernelInfo.KernelName) ? "<unknown>" : kernelInfo.KernelName;
        var totalSharedBytes = (long)kernelInfo.StaticSharedMemBytes + kernelInfo.DynamicSharedMemBytes;
        var regEstimateAtMin = (long)kernelInfo.NumRegs * minCandidate;
        return $"G#: cooperative kernel '{kernelName}' cannot fit even the smallest candidate block size " +
               $"({minCandidate} threads) on sm_{kernelInfo.DeviceMajor}{kernelInfo.DeviceMinor}: " +
               $"requires {kernelInfo.NumRegs} registers/thread (~{regEstimateAtMin} registers/block) and " +
               $"{totalSharedBytes} bytes shared memory ({kernelInfo.StaticSharedMemBytes} static + " +
               $"{kernelInfo.DynamicSharedMemBytes} dynamic); SM limits are " +
               $"{kernelInfo.MaxRegistersPerMultiprocessor} registers/SM, " +
               $"{kernelInfo.MaxRegistersPerBlock} registers/block, " +
               $"{kernelInfo.MaxSharedMemPerMultiprocessor} bytes shared memory/SM, " +
               $"{kernelInfo.MaxThreadsPerBlock} max threads/block.";
    }
}
