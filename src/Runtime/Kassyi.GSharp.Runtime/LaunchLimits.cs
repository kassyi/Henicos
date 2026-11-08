using System.ComponentModel;

namespace Kassyi.GSharp.Runtime;

/// <summary>
/// Checks the dimensions of a kernel launch against the limits that every GPU enforces. The generated launch
/// methods call it before any work, on CUDA and on both CPU paths, so that every path rejects the same launches in
/// the same way.
/// </summary>
/// <remarks>
/// The CPU paths accept every launch that the GPU accepts: the limits here are the only launch limits, and the run
/// time or the memory of the machine never adds one. The managed path still keeps a context for every thread of the
/// grid in one array (<see cref="GetManagedContextCount"/>), a limit of that implementation, not of the launch.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class LaunchLimits
{
    /// <summary>The largest <c>gridDimX</c> of every GPU (2^31 - 1).</summary>
    public const uint MaxGridDimX = int.MaxValue;

    /// <summary>The largest <c>gridDimY</c> and <c>gridDimZ</c> of every GPU.</summary>
    public const uint MaxGridDimYZ = 65535;

    /// <summary>The largest <c>blockDimX</c> and <c>blockDimY</c> of every GPU.</summary>
    public const uint MaxBlockDimXY = 1024;

    /// <summary>The largest <c>blockDimZ</c> of every GPU.</summary>
    public const uint MaxBlockDimZ = 64;

    /// <summary>The most threads in one block on every GPU.</summary>
    public const uint MaxThreadsPerBlock = 1024;

    /// <summary>
    /// Throws when a launch dimension is 0 or above the limit of every GPU, and returns the number of blocks of the
    /// grid.
    /// </summary>
    /// <param name="gridDimX">The number of blocks in x.</param>
    /// <param name="gridDimY">The number of blocks in y.</param>
    /// <param name="gridDimZ">The number of blocks in z.</param>
    /// <param name="blockDimX">The number of threads per block in x.</param>
    /// <param name="blockDimY">The number of threads per block in y.</param>
    /// <param name="blockDimZ">The number of threads per block in z.</param>
    /// <param name="kernelName">The kernel as <c>Type.Method</c>, for the exception message.</param>
    /// <returns>
    /// <c>gridDimX * gridDimY * gridDimZ</c>, counted in 64 bits: within the limits it is below 2^63, so it never
    /// wraps.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is 0 or above its limit, or the block has more than <see cref="MaxThreadsPerBlock"/> threads; the
    /// parameter name is the dimension.
    /// </exception>
    public static long CheckLaunch(uint gridDimX, uint gridDimY, uint gridDimZ, uint blockDimX, uint blockDimY,
        uint blockDimZ, string kernelName)
    {
        ThrowIfOutOfRange(gridDimX, MaxGridDimX, "gridDimX", "a grid can have at most 2147483647 blocks in x",
            kernelName);
        ThrowIfOutOfRange(gridDimY, MaxGridDimYZ, "gridDimY", "a grid can have at most 65535 blocks in y",
            kernelName);
        ThrowIfOutOfRange(gridDimZ, MaxGridDimYZ, "gridDimZ", "a grid can have at most 65535 blocks in z",
            kernelName);
        ThrowIfOutOfRange(blockDimX, MaxBlockDimXY, "blockDimX", "a block can have at most 1024 threads in x",
            kernelName);
        ThrowIfOutOfRange(blockDimY, MaxBlockDimXY, "blockDimY", "a block can have at most 1024 threads in y",
            kernelName);
        ThrowIfOutOfRange(blockDimZ, MaxBlockDimZ, "blockDimZ", "a block can have at most 64 threads in z",
            kernelName);

        // Each factor is at most 1024 here, so the product cannot wrap
        var threadsPerBlock = blockDimX * blockDimY * blockDimZ;
        if (threadsPerBlock > MaxThreadsPerBlock)
        {
            throw new ArgumentOutOfRangeException(nameof(blockDimX),
                $"G#: a block of {blockDimX} x {blockDimY} x {blockDimZ} threads exceeds the " +
                $"{MaxThreadsPerBlock} threads per block of every GPU ({kernelName}).");
        }

        return (long)gridDimX * gridDimY * gridDimZ;
    }

    /// <summary>
    /// The number of thread contexts of a launch on the managed CPU path, which keeps one for every thread of the
    /// grid in one array. For generated code only.
    /// </summary>
    /// <param name="totalBlocks">The number of blocks, from <see cref="CheckLaunch"/>.</param>
    /// <param name="blockDimX">The number of threads per block in x.</param>
    /// <param name="blockDimY">The number of threads per block in y.</param>
    /// <param name="blockDimZ">The number of threads per block in z.</param>
    /// <param name="kernelName">The kernel as <c>Type.Method</c>, for the exception message.</param>
    /// <returns>The number of threads of the launch.</returns>
    /// <exception cref="NotSupportedException">
    /// The launch has more threads than one array holds (<see cref="Array.MaxLength"/>), a limit of the managed
    /// path only.
    /// </exception>
    public static int GetManagedContextCount(long totalBlocks, uint blockDimX, uint blockDimY, uint blockDimZ,
        string kernelName)
    {
        // At most 1024 threads per block after CheckLaunch, so the comparison below never wraps
        long threadsPerBlock = (long)blockDimX * blockDimY * blockDimZ;
        if (totalBlocks > Array.MaxLength / threadsPerBlock)
        {
            throw new NotSupportedException(
                $"G#: the launch has {totalBlocks} blocks of {threadsPerBlock} threads, more than the " +
                $"{Array.MaxLength} threads that the managed CPU path can run in one launch ({kernelName}). " +
                "This is a limit of the managed path, which keeps a context for every thread of the grid in one " +
                "array; the native CPU path and the GPU run the launch. " +
                "See https://github.com/kassyi/Henicos/issues/1443 .");
        }

        return (int)(totalBlocks * threadsPerBlock);
    }

    private static void ThrowIfOutOfRange(uint value, uint max, string name, string limit, string kernelName)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(name,
                $"G#: {name} is 0; every launch dimension must be at least 1 ({kernelName}).");
        }

        if (value > max)
        {
            throw new ArgumentOutOfRangeException(name,
                $"G#: {name} is {value}; {limit} on every GPU ({kernelName}).");
        }
    }
}
