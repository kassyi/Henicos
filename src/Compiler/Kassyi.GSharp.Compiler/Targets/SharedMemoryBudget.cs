namespace Kassyi.GSharp.Compiler.Targets;

/// <summary>
/// The shared memory figures of a compute capability that limit how many blocks fit on one multiprocessor.
/// </summary>
/// <param name="PerMultiprocessor">The shared memory of one multiprocessor, in bytes.</param>
/// <param name="ReservedPerBlock">The bytes the driver reserves for each resident block.</param>
/// <param name="AllocationGranularity">The unit, in bytes, shared memory is allocated to a block in.</param>
internal readonly record struct SharedMemoryBudget(
    int PerMultiprocessor,
    int ReservedPerBlock,
    int AllocationGranularity)
{
    /// <summary>
    /// How many blocks that each use <paramref name="bytesPerBlock"/> bytes of shared memory fit on one multiprocessor
    /// by shared memory alone: the multiprocessor's shared memory divided by the block's bytes plus the reserved bytes,
    /// rounded up to the allocation unit; <see cref="int.MaxValue"/> when a block takes no shared memory at all.
    /// </summary>
    public int ResidentBlocks(long bytesPerBlock)
    {
        var allocated = bytesPerBlock + ReservedPerBlock;
        if (allocated == 0)
        {
            return int.MaxValue;
        }

        var rounded = (allocated + AllocationGranularity - 1) / AllocationGranularity * AllocationGranularity;
        return (int)(PerMultiprocessor / rounded);
    }

    /// <summary>
    /// Whether this budget never fits more blocks than <paramref name="other"/>, for any number of bytes per block:
    /// no more shared memory, at least as many reserved bytes, and an allocation unit that is a multiple of the other.
    /// </summary>
    public bool IsAtMost(SharedMemoryBudget other) =>
        PerMultiprocessor <= other.PerMultiprocessor &&
        ReservedPerBlock >= other.ReservedPerBlock &&
        AllocationGranularity % other.AllocationGranularity == 0;
}
