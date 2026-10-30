#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The grid and block of a differential launch, and the number of rows (threads that have work) the inputs hold. A
/// linear thread id numbers the threads block by block, x fastest inside a block and among the blocks, the order of the
/// managed launcher.
/// </summary>
/// <param name="GridX">Blocks along X.</param>
/// <param name="GridY">Blocks along Y.</param>
/// <param name="GridZ">Blocks along Z.</param>
/// <param name="BlockX">Threads per block along X.</param>
/// <param name="BlockY">Threads per block along Y.</param>
/// <param name="BlockZ">Threads per block along Z.</param>
/// <param name="Rows">The number of input rows; threads past it must return without writing.</param>
public sealed record LaunchShape(uint GridX, uint GridY, uint GridZ, uint BlockX, uint BlockY, uint BlockZ, int Rows)
{
    /// <summary>A one-dimensional launch with enough blocks of <paramref name="blockX"/> threads for every row.</summary>
    /// <param name="rows">The number of rows.</param>
    /// <param name="blockX">Threads per block.</param>
    public static LaunchShape ForRows(int rows, uint blockX)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfZero(blockX);
        return new LaunchShape(((uint)rows + blockX - 1) / blockX, 1, 1, blockX, 1, 1, rows);
    }

    /// <summary>Gets the number of threads per block.</summary>
    public int ThreadsPerBlock => checked((int)(BlockX * BlockY * BlockZ));

    /// <summary>Gets the number of threads in the launch.</summary>
    public int ThreadCount => checked((int)(GridX * GridY * GridZ) * ThreadsPerBlock);

    /// <summary>Returns the coordinates of the thread with the given linear id.</summary>
    /// <param name="linearId">The id, in <c>[0, ThreadCount)</c>.</param>
    public GpuThreadContext ContextOf(int linearId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(linearId);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(linearId, ThreadCount);
        int block = linearId / ThreadsPerBlock;
        int thread = linearId % ThreadsPerBlock;
        int bx = (int)BlockX;
        int by = (int)BlockY;
        int gx = (int)GridX;
        int gy = (int)GridY;
        return new GpuThreadContext(
            thread % bx, thread / bx % by, thread / (bx * by),
            block % gx, block / gx % gy, block / (gx * gy),
            bx, by, (int)BlockZ,
            gx, gy, (int)GridZ
        );
    }

    /// <inheritdoc/>
    public override string ToString() => $"grid=({GridX},{GridY},{GridZ}) block=({BlockX},{BlockY},{BlockZ})";
}
