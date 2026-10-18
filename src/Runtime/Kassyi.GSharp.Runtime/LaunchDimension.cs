namespace Kassyi.GSharp.Runtime;

/// <summary>
/// Provides utility methods for calculating GPU kernel launch grid and block dimensions.
/// </summary>
public static class LaunchDimension
{
    /// <summary>
    /// The default number of threads per block for 1D kernel launches (256).
    /// </summary>
    public const int DefaultThreadsPerBlock = 256;

    /// <summary>
    /// The hardware maximum number of threads per block (1024).
    /// </summary>
    public const int MaxThreadsPerBlock = 1024;

    /// <summary>
    /// Calculates the 1D grid and block dimensions required to execute the specified number of total threads.
    /// </summary>
    /// <param name="totalThreads">The total number of threads to dispatch.</param>
    /// <param name="threadsPerBlock">The number of threads per block. Defaults to <see cref="DefaultThreadsPerBlock"/>.</param>
    /// <returns>A tuple containing the grid dimension and block dimension.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="totalThreads"/> is negative, or <paramref name="threadsPerBlock"/> is non-positive or exceeds <see cref="MaxThreadsPerBlock"/>.</exception>
    public static (uint Grid, uint Block) Calculate1D(int totalThreads, int threadsPerBlock = DefaultThreadsPerBlock)
    {
        if (totalThreads < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalThreads), "Total threads cannot be negative.");
        }

        if (threadsPerBlock <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threadsPerBlock), "Threads per block must be greater than zero.");
        }

        if (threadsPerBlock > MaxThreadsPerBlock)
        {
            throw new ArgumentOutOfRangeException(nameof(threadsPerBlock), "Threads per block cannot exceed device limit of 1024.");
        }

        if (totalThreads == 0)
        {
            return (0, 0); // Skip launch
        }

        var block = (uint)threadsPerBlock;
        var grid = ((uint)totalThreads + block - 1u) / block;

        return (grid, block);
    }
}
