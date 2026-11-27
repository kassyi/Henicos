using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath;

/// <summary>
/// Test-facing launch shapes and CUDA graph playback counters recorded on a <see cref="GpuWorkspace"/>.
/// </summary>
internal sealed class GpuWorkspaceLaunchRecord
{
    private uint _lastModPowNttBlockDim;
    private uint _lastInverseSqrtMegaBlockDim;
    private uint _lastInverseSqrtMegaGridDim;
    private uint _lastInverseSqrtMegaSharedMemBytes;
    private uint _lastReciprocalMegaBlockDim;
    private uint _lastReciprocalMegaGridDim;
    private uint _lastReciprocalMegaSharedMemBytes;
    private long _modPowGraphLaunches;

    /// <summary>
    /// Gets the block size of the most recent ModPow NTT mega-kernel launch on this workspace, which the tests read to
    /// verify the launcher's block-size rule.
    /// </summary>
    internal uint LastModPowNttBlockDim => Volatile.Read(ref _lastModPowNttBlockDim);

    /// <summary>Records the block size used to launch the ModPow NTT mega-kernel.</summary>
    internal void RecordModPowNttBlockDim(uint blockDim) => Volatile.Write(ref _lastModPowNttBlockDim, blockDim);

    /// <summary>
    /// Gets the launch shape of the most recent <c>InverseSqrtNewtonRaphsonMegaKernel</c> launch on this workspace,
    /// which tests read to verify block and grid dimensions.
    /// </summary>
    internal CooperativeMegaKernelLauncher.LaunchShape LastInverseSqrtMegaLaunchShape =>
        new(
            Volatile.Read(ref _lastInverseSqrtMegaBlockDim),
            Volatile.Read(ref _lastInverseSqrtMegaGridDim),
            Volatile.Read(ref _lastInverseSqrtMegaSharedMemBytes));

    /// <summary>Records the launch shape used to launch <c>InverseSqrtNewtonRaphsonMegaKernel</c>.</summary>
    internal void RecordInverseSqrtMegaLaunchShape(uint blockDim, uint gridDim, uint sharedMemBytes)
    {
        Volatile.Write(ref _lastInverseSqrtMegaBlockDim, blockDim);
        Volatile.Write(ref _lastInverseSqrtMegaGridDim, gridDim);
        Volatile.Write(ref _lastInverseSqrtMegaSharedMemBytes, sharedMemBytes);
    }

    /// <summary>
    /// Gets the launch shape of the most recent <c>ReciprocalNewtonRaphsonMegaKernel</c> launch on this workspace,
    /// which tests read to verify block and grid dimensions.
    /// </summary>
    internal CooperativeMegaKernelLauncher.LaunchShape LastReciprocalMegaLaunchShape =>
        new(
            Volatile.Read(ref _lastReciprocalMegaBlockDim),
            Volatile.Read(ref _lastReciprocalMegaGridDim),
            Volatile.Read(ref _lastReciprocalMegaSharedMemBytes));

    /// <summary>Records the launch shape used to launch <c>ReciprocalNewtonRaphsonMegaKernel</c>.</summary>
    internal void RecordReciprocalMegaLaunchShape(uint blockDim, uint gridDim, uint sharedMemBytes)
    {
        Volatile.Write(ref _lastReciprocalMegaBlockDim, blockDim);
        Volatile.Write(ref _lastReciprocalMegaGridDim, gridDim);
        Volatile.Write(ref _lastReciprocalMegaSharedMemBytes, sharedMemBytes);
    }

    /// <summary>
    /// Gets the number of CUDA graph launches executed by <c>ModPowZeroSyncController</c> on this workspace, which
    /// tests read to verify graph playback.
    /// </summary>
    internal long ModPowGraphLaunches => Volatile.Read(ref _modPowGraphLaunches);

    /// <summary>Counts a CUDA graph launch executed by <c>ModPowZeroSyncController</c>.</summary>
    internal void CountModPowGraphLaunch() => Interlocked.Increment(ref _modPowGraphLaunches);
}
