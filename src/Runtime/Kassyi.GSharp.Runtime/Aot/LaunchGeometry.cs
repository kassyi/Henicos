using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Runtime.Aot;

// The dimensions of a launch and the coordinates of a linear thread index
internal readonly struct LaunchGeometry
{
    private readonly long _gridX;
    private readonly long _gridY;
    private readonly long _gridZ;
    private readonly long _blockX;
    private readonly long _blockY;
    private readonly long _blockZ;
    private readonly long _threadsPerBlock;

    public LaunchGeometry(uint gridDimX, uint gridDimY, uint gridDimZ, uint blockDimX, uint blockDimY,
        uint blockDimZ)
    {
        ArgumentOutOfRangeException.ThrowIfZero(gridDimX);
        ArgumentOutOfRangeException.ThrowIfZero(gridDimY);
        ArgumentOutOfRangeException.ThrowIfZero(gridDimZ);
        ArgumentOutOfRangeException.ThrowIfZero(blockDimX);
        ArgumentOutOfRangeException.ThrowIfZero(blockDimY);
        ArgumentOutOfRangeException.ThrowIfZero(blockDimZ);
        (_gridX, _gridY, _gridZ) = (gridDimX, gridDimY, gridDimZ);
        (_blockX, _blockY, _blockZ) = (blockDimX, blockDimY, blockDimZ);
        _threadsPerBlock = checked(_blockX * _blockY * _blockZ);
        TotalBlocks = checked(_gridX * _gridY * _gridZ);
        ThreadCount = checked(TotalBlocks * _threadsPerBlock);
    }

    public long TotalBlocks { get; }

    public long ThreadsPerBlock => _threadsPerBlock;

    public long ThreadCount { get; }

    public CSharpLaunchDimensions Dimensions(int threadCount) => new(
        (uint)_gridX, (uint)_gridY, (uint)_gridZ, (uint)_blockX, (uint)_blockY, (uint)_blockZ, threadCount);

    // The same arrangement as the managed launcher and the GPU: x fastest, for the threads and for the blocks
    public GpuThreadContext ContextOf(long id)
    {
        var block = id / _threadsPerBlock;
        var t = id % _threadsPerBlock;
        return new GpuThreadContext(
            (int)(t % _blockX), (int)(t / _blockX % _blockY), (int)(t / (_blockX * _blockY)),
            (int)(block % _gridX), (int)(block / _gridX % _gridY), (int)(block / (_gridX * _gridY)),
            (int)_blockX, (int)_blockY, (int)_blockZ,
            (int)_gridX, (int)_gridY, (int)_gridZ
        );
    }
}
