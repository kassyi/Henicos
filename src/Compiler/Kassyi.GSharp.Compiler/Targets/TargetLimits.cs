namespace Kassyi.GSharp.Compiler.Targets;

/// <summary>
/// The launch limits of the GPUs the project's fatbin may run on (<c>GSharpTargetArchitectures</c>): the strictest
/// value over every compute capability of <see cref="Architectures"/>, so that a launch passes only when every one
/// of those GPUs accepts it.
/// </summary>
/// <remarks>
/// <see cref="GpuArchitectureLimits.Resolve"/> makes the value; it compares by value, so the generator steps that
/// depend on it rerun only when the setting changes. When no compute capability of the setting is in the table of
/// <see cref="GpuArchitectureLimits"/>, <see cref="IsKnown"/> is <see langword="false"/> and every limit is 0.
/// </remarks>
/// <param name="Architectures">The compute capabilities of the table the fatbin may run on, oldest first.</param>
/// <param name="MaxSharedMemoryPerBlockOptIn">The smallest shared memory per block with opt-in, in bytes.</param>
/// <param name="UnknownArchitectures">
/// The architectures of the setting (as written, <c>sm_72</c>, <c>compute_130</c>) whose compute capability the table
/// does not have; their limits are left out.
/// </param>
internal readonly record struct TargetLimits(
    EquatableArray<GpuArchitecture> Architectures,
    int MaxSharedMemoryPerBlockOptIn,
    EquatableArray<string> UnknownArchitectures)
{
    /// <summary>Whether at least one compute capability the fatbin may run on is in the table.</summary>
    public bool IsKnown => !Architectures.IsDefaultOrEmpty;

    /// <summary>
    /// The SASS name (<c>sm_86</c>) of the oldest compute capability whose opt-in limit is
    /// <see cref="MaxSharedMemoryPerBlockOptIn"/>, to name the GPU that rejects a launch; empty when
    /// <see cref="IsKnown"/> is <see langword="false"/>.
    /// </summary>
    public string SharedMemoryLimitArchitecture => Architectures
        .AsStackEnumerable()
        .TryFirst(
            MaxSharedMemoryPerBlockOptIn,
            static (row, limit) => row.MaxSharedMemoryPerBlockOptIn == limit,
            out var strictest
        )
        ? strictest.Name
        : "";
}
