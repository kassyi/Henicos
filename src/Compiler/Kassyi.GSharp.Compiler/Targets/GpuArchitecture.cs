namespace Kassyi.GSharp.Compiler.Targets;

/// <summary>
/// The launch limits of the GPUs of one compute capability, in bytes and counts, as the CUDA Programming Guide
/// ("Compute Capabilities", technical specifications) lists them.
/// </summary>
/// <param name="Major">The major version of the compute capability (8 for 8.6).</param>
/// <param name="Minor">The minor version of the compute capability (6 for 8.6).</param>
/// <param name="MaxBlocksPerMultiprocessor">The most blocks resident on one multiprocessor at once.</param>
/// <param name="MaxThreadsPerMultiprocessor">The most threads resident on one multiprocessor at once.</param>
/// <param name="SharedMemoryPerMultiprocessor">The shared memory of one multiprocessor.</param>
/// <param name="MaxSharedMemoryPerBlockOptIn">
/// The most shared memory (static and dynamic) one block may use after opting in above 48 KB.
/// </param>
/// <param name="ReservedSharedMemoryPerBlock">
/// The shared memory the driver reserves for each resident block, on top of what the block uses.
/// </param>
/// <param name="SharedMemoryAllocationGranularity">The unit shared memory is allocated to a block in.</param>
/// <param name="IsTegra">
/// Whether the GPUs are Tegra (integrated) parts, which run no SASS of another compute capability, and whose SASS no
/// other GPU runs.
/// </param>
internal readonly record struct GpuArchitecture(
    int Major,
    int Minor,
    int MaxBlocksPerMultiprocessor,
    int MaxThreadsPerMultiprocessor,
    int SharedMemoryPerMultiprocessor,
    int MaxSharedMemoryPerBlockOptIn,
    int ReservedSharedMemoryPerBlock,
    int SharedMemoryAllocationGranularity,
    bool IsTegra = false)
{
    /// <summary>The SASS name of the compute capability (<c>sm_86</c>).</summary>
    public string Name => $"sm_{Major}{Minor}";

    /// <summary>Whether the compute capability is <paramref name="major"/>.<paramref name="minor"/> or newer.</summary>
    public bool IsAtLeast(int major, int minor) => Major > major || (Major == major && Minor >= minor);

    /// <summary>The shared memory figures that limit how many blocks fit on one multiprocessor.</summary>
    public SharedMemoryBudget SharedMemoryBudget =>
        new(SharedMemoryPerMultiprocessor, ReservedSharedMemoryPerBlock, SharedMemoryAllocationGranularity);
}
