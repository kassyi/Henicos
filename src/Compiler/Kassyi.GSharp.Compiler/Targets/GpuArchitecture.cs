namespace Kassyi.GSharp.Compiler.Targets;

/// <summary>
/// The shared memory limit of the GPUs of one compute capability, as the CUDA Programming Guide ("Compute
/// Capabilities", memory information) lists it.
/// </summary>
/// <param name="Major">The major version of the compute capability (8 for 8.6).</param>
/// <param name="Minor">The minor version of the compute capability (6 for 8.6).</param>
/// <param name="MaxSharedMemoryPerBlockOptIn">
/// The most shared memory (static and dynamic), in bytes, one block may use after opting in above 48 KB.
/// </param>
/// <param name="IsTegra">
/// Whether the GPUs are Tegra (integrated) parts, which run no SASS of another compute capability, and whose SASS no
/// other GPU runs.
/// </param>
internal readonly record struct GpuArchitecture(
    int Major,
    int Minor,
    int MaxSharedMemoryPerBlockOptIn,
    bool IsTegra = false)
{
    /// <summary>The SASS name of the compute capability (<c>sm_86</c>).</summary>
    public string Name => $"sm_{Major}{Minor}";

    /// <summary>Whether the compute capability is <paramref name="major"/>.<paramref name="minor"/> or newer.</summary>
    public bool IsAtLeast(int major, int minor) => Major > major || (Major == major && Minor >= minor);
}
