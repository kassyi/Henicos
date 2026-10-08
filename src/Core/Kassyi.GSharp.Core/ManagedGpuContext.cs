using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Core;

/// <summary>
/// Provides a managed execution context for evaluating GPU kernels directly on the .NET host CPU without native toolchains.
/// </summary>
public static class ManagedGpuContext
{
    /// <summary>
    /// Gets or sets a value indicating whether the current thread is executing within a managed cooperative scheduler.
    /// </summary>
    [ThreadStatic]
    public static bool IsActive;

    /// <summary>
    /// Gets or sets the x-dimension thread index within the current thread block.
    /// </summary>
    [ThreadStatic]
    public static int ThreadIdxX;

    /// <summary>
    /// Gets or sets the y-dimension thread index within the current thread block.
    /// </summary>
    [ThreadStatic]
    public static int ThreadIdxY;

    /// <summary>
    /// Gets or sets the z-dimension thread index within the current thread block.
    /// </summary>
    [ThreadStatic]
    public static int ThreadIdxZ;

    /// <summary>
    /// Gets or sets the x-dimension block index within the grid.
    /// </summary>
    [ThreadStatic]
    public static int BlockIdxX;

    /// <summary>
    /// Gets or sets the y-dimension block index within the grid.
    /// </summary>
    [ThreadStatic]
    public static int BlockIdxY;

    /// <summary>
    /// Gets or sets the z-dimension block index within the grid.
    /// </summary>
    [ThreadStatic]
    public static int BlockIdxZ;

    /// <summary>
    /// Gets or sets the x-dimension size of each block.
    /// </summary>
    [ThreadStatic]
    public static int BlockDimX;

    /// <summary>
    /// Gets or sets the y-dimension size of each block.
    /// </summary>
    [ThreadStatic]
    public static int BlockDimY;

    /// <summary>
    /// Gets or sets the z-dimension size of each block.
    /// </summary>
    [ThreadStatic]
    public static int BlockDimZ;

    /// <summary>
    /// Gets or sets the x-dimension size of the grid.
    /// </summary>
    [ThreadStatic]
    public static int GridDimX;

    /// <summary>
    /// Gets or sets the y-dimension size of the grid.
    /// </summary>
    [ThreadStatic]
    public static int GridDimY;

    /// <summary>
    /// Gets or sets the z-dimension size of the grid.
    /// </summary>
    [ThreadStatic]
    public static int GridDimZ;

    // Shared memory buffer per thread (each physical thread represents a block during managed execution loop)
    [ThreadStatic]
    private static byte[][]? s_perBlockSharedMemoryBuffer;

    [ThreadStatic]
    private static int[]? s_perBlockSharedMemoryOffset;

    [ThreadStatic]
    private static int[][]? s_perBlockAllocationOffsets;

    [ThreadStatic]
    private static int[]? s_perBlockMaxAllocationIndex;

    [ThreadStatic]
    private static int s_allocationIndex;

    private static int LinearBlockId => BlockIdxZ * GridDimY * GridDimX + BlockIdxY * GridDimX + BlockIdxX;

    /// <summary>
    /// Initializes or resets the shared memory arrays for the entire grid execution.
    /// </summary>
    /// <param name="totalBlocks">The total number of blocks in the grid.</param>
    public static void InitializeGrid(int totalBlocks)
    {
        if (s_perBlockSharedMemoryBuffer == null ||
            s_perBlockSharedMemoryOffset == null ||
            s_perBlockAllocationOffsets == null ||
            s_perBlockMaxAllocationIndex == null ||
            s_perBlockSharedMemoryBuffer.Length < totalBlocks)
        {
            s_perBlockSharedMemoryBuffer = new byte[totalBlocks][];
            s_perBlockSharedMemoryOffset = new int[totalBlocks];
            s_perBlockAllocationOffsets = new int[totalBlocks][];
            s_perBlockMaxAllocationIndex = new int[totalBlocks];
        }
        else
        {
            Array.Clear(s_perBlockSharedMemoryOffset, 0, totalBlocks);
            Array.Clear(s_perBlockMaxAllocationIndex, 0, totalBlocks);
            // We don't need to clear the actual buffers, just the offsets
        }
    }

    /// <summary>
    /// Resets the allocation index so the next thread can reuse the same shared memory buffers.
    /// </summary>
    public static void ResetAllocationIndex() => s_allocationIndex = 0;

    /// <summary>
    /// Allocates a contiguous span of shared memory from the current block's buffer.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type of the shared memory buffer.</typeparam>
    /// <param name="count">The number of elements to allocate.</param>
    /// <returns>A contiguous <see cref="Span{T}"/> pointing to the allocated shared memory region.</returns>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static Span<T> AllocateSharedMemory<T>(int count) where T : unmanaged
    {
        if (count <= 0)
        {
            return [];
        }

        var bytesNeeded = count * Unsafe.SizeOf<T>();
        var blockId = LinearBlockId;

        if (s_perBlockAllocationOffsets != null)
        {
            var allocationOffsets = s_perBlockAllocationOffsets[blockId];

            var currentIndex = s_allocationIndex++;

            // Ensure buffer exists and is large enough

            var buffer = s_perBlockSharedMemoryBuffer?[blockId];
            if (buffer == null)
            {
                buffer = [];
                s_perBlockSharedMemoryBuffer![blockId] = buffer;
            }

            if (currentIndex < s_perBlockMaxAllocationIndex![blockId])
            {
                // Reuse existing allocation for this index
                var existingOffset = allocationOffsets[currentIndex];
                return MemoryMarshal.Cast<byte, T>(buffer.AsSpan(existingOffset, bytesNeeded));
            }

            if (s_perBlockSharedMemoryOffset![blockId] + bytesNeeded > buffer.Length)
            {
                // Expand buffer if necessary
                Array.Resize(ref s_perBlockSharedMemoryBuffer![blockId],
                    Math.Max(buffer.Length * 2, s_perBlockSharedMemoryOffset[blockId] + bytesNeeded));
                buffer = s_perBlockSharedMemoryBuffer[blockId];
            }

            var newOffset = s_perBlockSharedMemoryOffset[blockId];
            var byteSpan = buffer.AsSpan(newOffset, bytesNeeded);
            s_perBlockSharedMemoryOffset[blockId] += bytesNeeded;

            if (currentIndex >= allocationOffsets.Length)
            {
                Array.Resize(ref s_perBlockAllocationOffsets[blockId], Math.Max(16, allocationOffsets.Length * 2));
                allocationOffsets = s_perBlockAllocationOffsets[blockId];
            }

            allocationOffsets[currentIndex] = newOffset;
            s_perBlockMaxAllocationIndex[blockId]++;

            return MemoryMarshal.Cast<byte, T>(byteSpan);
        }

        throw new InvalidOperationException("Grid not initialized. Call InitializeGrid first.");
    }

    /// <summary>
    /// Sets the thread coordinates for the current cooperative scheduler step.
    /// </summary>
    /// <param name="tx">The x-dimension thread index (threadIdx.x).</param>
    /// <param name="ty">The y-dimension thread index (threadIdx.y).</param>
    /// <param name="tz">The z-dimension thread index (threadIdx.z).</param>
    /// <param name="bx">The x-dimension block index (blockIdx.x).</param>
    /// <param name="by">The y-dimension block index (blockIdx.y).</param>
    /// <param name="bz">The z-dimension block index (blockIdx.z).</param>
    /// <param name="bDimX">The x-dimension size of the thread block (blockDim.x).</param>
    /// <param name="bDimY">The y-dimension size of the thread block (blockDim.y).</param>
    /// <param name="bDimZ">The z-dimension size of the thread block (blockDim.z).</param>
    /// <param name="gDimX">The x-dimension size of the grid (gridDim.x).</param>
    /// <param name="gDimY">The y-dimension size of the grid (gridDim.y).</param>
    /// <param name="gDimZ">The z-dimension size of the grid (gridDim.z).</param>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void SetThreadContext(
        int tx, int ty, int tz,
        int bx, int by, int bz,
        int bDimX, int bDimY, int bDimZ,
        int gDimX, int gDimY, int gDimZ)
    {
        IsActive = true;

        ThreadIdxX = tx;
        ThreadIdxY = ty;
        ThreadIdxZ = tz;

        BlockIdxX = bx;
        BlockIdxY = by;
        BlockIdxZ = bz;

        BlockDimX = bDimX;
        BlockDimY = bDimY;
        BlockDimZ = bDimZ;

        GridDimX = gDimX;
        GridDimY = gDimY;
        GridDimZ = gDimZ;
    }

    /// <summary>
    /// Clears the thread context when execution is completed.
    /// </summary>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void ClearThreadContext() => IsActive = false;

    // Mocks for Warp Shuffle operations in Managed Execution

    [ThreadStatic]
    private static byte[]? s_warpExchangeBuffer;

    private static Span<byte> GetWarpExchangeBuffer()
    {
        s_warpExchangeBuffer ??= new byte[1024 * 8]; // max 1024 threads per block * 8 bytes (double)
        return s_warpExchangeBuffer;
    }

    /// <summary>
    /// Internal mock for warp exchange assignment. Do not use directly.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to assign.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void __gsharp_warp_exchange_assign<T>(T value) where T : unmanaged
    {
        var linearTid = ThreadIdxZ * BlockDimY * BlockDimX + ThreadIdxY * BlockDimX + ThreadIdxX;
        Unsafe.WriteUnaligned(ref GetWarpExchangeBuffer()[linearTid * 8], value);
    }

    /// <summary>
    /// Internal mock for warp shuffle down. Do not use directly.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The thread mask.</param>
    /// <param name="val">The value to shuffle.</param>
    /// <param name="delta">The lane offset.</param>
    /// <param name="width">The logical warp width.</param>
    /// <returns>The shuffled value.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static dynamic __gsharp_warp_shuffle_read_down_sync<TContext, T>(TContext ctx, uint mask, T val, uint delta,
        int width = 32) where T : unmanaged
    {
        var linearTid = ThreadIdxZ * BlockDimY * BlockDimX + ThreadIdxY * BlockDimX + ThreadIdxX;
        var laneId = linearTid % 32;
        var warpStartTid = linearTid - laneId;

        var logicalLane = laneId % width;
        var targetLogicalLane = logicalLane + (int)delta;

        if (targetLogicalLane < width)
        {
            var globalSrcLane = laneId - logicalLane + targetLogicalLane;
            if (globalSrcLane < 32)
            {
                var targetTid = warpStartTid + globalSrcLane;
                return Unsafe.ReadUnaligned<T>(ref GetWarpExchangeBuffer()[targetTid * 8]);
            }
        }

        return val;
    }

    /// <summary>
    /// Internal mock for warp shuffle up. Do not use directly.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The thread mask.</param>
    /// <param name="val">The value to shuffle.</param>
    /// <param name="delta">The lane offset.</param>
    /// <param name="width">The logical warp width.</param>
    /// <returns>The shuffled value.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static dynamic __gsharp_warp_shuffle_read_up_sync<TContext, T>(TContext ctx, uint mask, T val, uint delta,
        int width = 32) where T : unmanaged
    {
        var linearTid = ThreadIdxZ * BlockDimY * BlockDimX + ThreadIdxY * BlockDimX + ThreadIdxX;
        var laneId = linearTid % 32;
        var warpStartTid = linearTid - laneId;

        var logicalLane = laneId % width;
        var targetLogicalLane = logicalLane - (int)delta;

        if (targetLogicalLane < 0)
        {
            return val;
        }

        var globalSrcLane = laneId - logicalLane + targetLogicalLane;
        if (globalSrcLane >= 32)
        {
            return val;
        }

        var targetTid = warpStartTid + globalSrcLane;
        return Unsafe.ReadUnaligned<T>(ref GetWarpExchangeBuffer()[targetTid * 8]);
    }

    /// <summary>
    /// Internal mock for warp shuffle XOR. Do not use directly.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The thread mask.</param>
    /// <param name="val">The value to shuffle.</param>
    /// <param name="delta">The lane XOR mask.</param>
    /// <param name="width">The logical warp width.</param>
    /// <returns>The shuffled value.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static dynamic __gsharp_warp_shuffle_read_xor_sync<TContext, T>(TContext ctx, uint mask, T val, uint delta,
        int width = 32) where T : unmanaged
    {
        var linearTid = ThreadIdxZ * BlockDimY * BlockDimX + ThreadIdxY * BlockDimX + ThreadIdxX;
        var laneId = linearTid % 32;
        var warpStartTid = linearTid - laneId;

        var logicalLane = laneId % width;
        var targetLogicalLane = logicalLane ^ (int)delta;

        if (targetLogicalLane >= width)
        {
            return val;
        }

        var globalSrcLane = laneId - logicalLane + targetLogicalLane;
        if (globalSrcLane >= 32)
        {
            return val;
        }

        var targetTid = warpStartTid + globalSrcLane;
        return Unsafe.ReadUnaligned<T>(ref GetWarpExchangeBuffer()[targetTid * 8]);
    }

    /// <summary>
    /// Internal mock for warp shuffle direct read. Do not use directly.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The thread mask.</param>
    /// <param name="val">The value to shuffle.</param>
    /// <param name="srcLane">The source lane ID.</param>
    /// <param name="width">The logical warp width.</param>
    /// <returns>The shuffled value.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static dynamic __gsharp_warp_shuffle_read_sync<TContext, T>(TContext ctx, uint mask, T val, uint srcLane,
        int width = 32) where T : unmanaged
    {
        var linearTid = ThreadIdxZ * BlockDimY * BlockDimX + ThreadIdxY * BlockDimX + ThreadIdxX;
        var laneId = linearTid % 32;
        var warpStartTid = linearTid - laneId;

        var logicalLane = laneId % width;
        var targetLogicalLane = (int)srcLane;

        if (targetLogicalLane >= width)
        {
            return val;
        }

        var globalSrcLane = laneId - logicalLane + targetLogicalLane;
        if (globalSrcLane >= 32)
        {
            return val;
        }

        var targetTid = warpStartTid + globalSrcLane;
        return Unsafe.ReadUnaligned<T>(ref GetWarpExchangeBuffer()[targetTid * 8]);
    }
}
