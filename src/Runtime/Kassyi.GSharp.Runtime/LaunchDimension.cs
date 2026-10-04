namespace Kassyi.GSharp.Runtime;

public static class LaunchDimension
{
    public static (uint Grid, uint Block) Calculate1D(int totalThreads, int threadsPerBlock = 256)
    {
        if (totalThreads < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalThreads), "Total threads cannot be negative.");
        }

        if (threadsPerBlock <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(threadsPerBlock), "Threads per block must be greater than zero.");
        }

        if (threadsPerBlock > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(threadsPerBlock), "Threads per block cannot exceed device limit of 1024.");
        }

        if (totalThreads == 0)
        {
            return (0, 0); // Skip launch
        }

        var block = (uint)threadsPerBlock;
        var grid = (uint)((totalThreads + threadsPerBlock - 1) / threadsPerBlock);

        return (grid, block);
    }
}
