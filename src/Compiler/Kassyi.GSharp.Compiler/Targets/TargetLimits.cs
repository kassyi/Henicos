namespace Kassyi.GSharp.Compiler.Targets;

/// <summary>
/// The launch limits of the GPUs the project's fatbin may run on (<c>GSharpTargetArchitectures</c>): for each limit,
/// the strictest value over every compute capability of <see cref="Architectures"/>, so that a launch passes only
/// when every one of those GPUs accepts it.
/// </summary>
/// <remarks>
/// <see cref="GpuArchitectureLimits.Resolve"/> makes the value; it compares by value, so the generator steps that
/// depend on it rerun only when the setting changes. When no compute capability of the setting is in the table of
/// <see cref="GpuArchitectureLimits"/>, <see cref="IsKnown"/> is <see langword="false"/> and every limit is 0.
/// </remarks>
/// <param name="Architectures">The compute capabilities of the table the fatbin may run on, oldest first.</param>
/// <param name="MaxSharedMemoryPerBlockOptIn">The smallest shared memory per block with opt-in, in bytes.</param>
/// <param name="MaxBlocksPerMultiprocessor">The fewest resident blocks per multiprocessor.</param>
/// <param name="MaxThreadsPerMultiprocessor">The fewest resident threads per multiprocessor.</param>
/// <param name="SharedMemoryBudgets">
/// The shared memory budgets of <see cref="Architectures"/> that may hold the fewest blocks: no budget in the list is
/// <see cref="SharedMemoryBudget.IsAtMost"/> another. The resident blocks by shared memory are the smallest
/// <see cref="SharedMemoryBudget.ResidentBlocks"/> over the list; taking the smallest shared memory, the largest
/// reservation and the largest unit of different compute capabilities would give fewer than any of them.
/// </param>
/// <param name="UnknownArchitectures">
/// The architectures of the setting (as written, <c>sm_72</c>, <c>compute_130</c>) whose compute capability the table
/// does not have; their limits are left out.
/// </param>
internal readonly record struct TargetLimits(
    EquatableArray<GpuArchitecture> Architectures,
    int MaxSharedMemoryPerBlockOptIn,
    int MaxBlocksPerMultiprocessor,
    int MaxThreadsPerMultiprocessor,
    EquatableArray<SharedMemoryBudget> SharedMemoryBudgets,
    EquatableArray<string> UnknownArchitectures)
{
    /// <summary>Whether at least one compute capability the fatbin may run on is in the table.</summary>
    public bool IsKnown => !Architectures.IsDefaultOrEmpty;

    /// <summary>
    /// How many blocks that each use <paramref name="bytesPerBlock"/> bytes of shared memory fit on one multiprocessor
    /// of every target GPU by shared memory alone; <see cref="int.MaxValue"/> when nothing limits them.
    /// </summary>
    public int ResidentBlocksBySharedMemory(long bytesPerBlock) => SharedMemoryBudgets
        .AsStackEnumerable()
        .Select(bytesPerBlock, static (budget, bytes) => budget.ResidentBlocks(bytes))
        .Aggregate(int.MaxValue, Math.Min);
}
