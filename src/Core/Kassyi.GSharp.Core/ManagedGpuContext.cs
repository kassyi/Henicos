using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Core;

/// <summary>
/// Represents the execution context coordinates for a single GPU thread.
/// </summary>
public readonly record struct GpuThreadContext(
    int ThreadIdxX, int ThreadIdxY, int ThreadIdxZ,
    int BlockIdxX, int BlockIdxY, int BlockIdxZ,
    int BlockDimX, int BlockDimY, int BlockDimZ,
    int GridDimX, int GridDimY, int GridDimZ);

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
    private static byte[]?[]? s_perBlockSharedMemoryBuffer;

    [ThreadStatic]
    private static int[]? s_perBlockSharedMemoryOffset;

    [ThreadStatic]
    private static ulong[]?[]? s_perBlockSiteKeys;

    [ThreadStatic]
    private static int[]?[]? s_perBlockSiteOffsets;

    [ThreadStatic]
    private static int[]? s_perBlockSiteCount;

    private static int LinearBlockId => BlockIdxZ * GridDimY * GridDimX + BlockIdxY * GridDimX + BlockIdxX;

    /// <summary>
    /// Initializes or resets the shared memory arrays for the entire grid execution.
    /// </summary>
    /// <param name="totalBlocks">The total number of blocks in the grid.</param>
    public static void InitializeGrid(int totalBlocks)
    {
        if (s_perBlockSharedMemoryBuffer == null ||
            s_perBlockSharedMemoryOffset == null ||
            s_perBlockSiteKeys == null ||
            s_perBlockSiteOffsets == null ||
            s_perBlockSiteCount == null)
        {
            s_perBlockSharedMemoryBuffer = new byte[]?[totalBlocks];
            s_perBlockSharedMemoryOffset = new int[totalBlocks];
            s_perBlockSiteKeys = new ulong[]?[totalBlocks];
            s_perBlockSiteOffsets = new int[]?[totalBlocks];
            s_perBlockSiteCount = new int[totalBlocks];
        }
        else
        {
            if (s_perBlockSharedMemoryBuffer.Length < totalBlocks)
            {
                Array.Resize(ref s_perBlockSharedMemoryBuffer, totalBlocks);
                Array.Resize(ref s_perBlockSharedMemoryOffset, totalBlocks);
                Array.Resize(ref s_perBlockSiteKeys, totalBlocks);
                Array.Resize(ref s_perBlockSiteOffsets, totalBlocks);
                Array.Resize(ref s_perBlockSiteCount, totalBlocks);
            }

            Array.Clear(s_perBlockSharedMemoryOffset, 0, totalBlocks);
            Array.Clear(s_perBlockSiteCount, 0, totalBlocks);
            // We don't need to clear the actual buffers, just the offsets
        }
    }

    /// <summary>
    /// Returns the shared memory of a <see cref="SharedMemory{T}"/> declaration in the current block, allocating it the
    /// first time the declaration is reached in the block.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type of the shared memory buffer.</typeparam>
    /// <param name="siteKey">The key that identifies the declaration. The G# compiler computes it for generated code.</param>
    /// <param name="count">The number of elements to allocate.</param>
    /// <returns>A contiguous <see cref="Span{T}"/> pointing to the shared memory region of the declaration.</returns>
    /// <remarks>
    /// [WHY] On the GPU a <c>__shared__</c> variable has static storage: one buffer per declaration per block, shared by
    /// every call of the function that declares it, from any call site and by every thread of the block. The buffer is
    /// therefore looked up by the declaration, not by the order of allocations, which differs between a caller and the
    /// device functions it calls and between threads that take different branches.
    /// </remarks>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static Span<T> AllocateSharedMemory<T>(ulong siteKey, int count) where T : unmanaged
    {
        if (count <= 0)
        {
            return [];
        }

        if (s_perBlockSharedMemoryBuffer == null ||
            s_perBlockSharedMemoryOffset == null ||
            s_perBlockSiteKeys == null ||
            s_perBlockSiteOffsets == null ||
            s_perBlockSiteCount == null)
        {
            throw new InvalidOperationException("Grid not initialized. Call InitializeGrid first.");
        }

        var bytesNeeded = count * Unsafe.SizeOf<T>();
        var blockId = LinearBlockId;
        var siteKeys = s_perBlockSiteKeys[blockId] ??= new ulong[16];
        var siteOffsets = s_perBlockSiteOffsets[blockId] ??= new int[16];
        var siteCount = s_perBlockSiteCount[blockId];
        var buffer = s_perBlockSharedMemoryBuffer[blockId] ??= [];

        for (var i = 0; i < siteCount; i++)
        {
            if (siteKeys[i] == siteKey)
            {
                return MemoryMarshal.Cast<byte, T>(buffer.AsSpan(siteOffsets[i], bytesNeeded));
            }
        }

        var newOffset = s_perBlockSharedMemoryOffset[blockId];
        if (newOffset + bytesNeeded > buffer.Length)
        {
            Array.Resize(ref s_perBlockSharedMemoryBuffer[blockId], Math.Max(buffer.Length * 2, newOffset + bytesNeeded));
            buffer = s_perBlockSharedMemoryBuffer[blockId] ?? throw new InvalidOperationException("Shared memory buffer was lost while resizing.");
        }

        if (siteCount == siteKeys.Length)
        {
            Array.Resize(ref siteKeys, siteCount * 2);
            Array.Resize(ref siteOffsets, siteCount * 2);
            s_perBlockSiteKeys[blockId] = siteKeys;
            s_perBlockSiteOffsets[blockId] = siteOffsets;
        }

        siteKeys[siteCount] = siteKey;
        siteOffsets[siteCount] = newOffset;
        s_perBlockSiteCount[blockId] = siteCount + 1;
        s_perBlockSharedMemoryOffset[blockId] = newOffset + bytesNeeded;

        return MemoryMarshal.Cast<byte, T>(buffer.AsSpan(newOffset, bytesNeeded));
    }

    /// <summary>
    /// Sets the thread coordinates for the current cooperative scheduler step.
    /// </summary>
    /// <param name="ctx">The GPU thread execution context.</param>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void SetThreadContext(in GpuThreadContext ctx)
    {
        IsActive = true;

        ThreadIdxX = ctx.ThreadIdxX;
        ThreadIdxY = ctx.ThreadIdxY;
        ThreadIdxZ = ctx.ThreadIdxZ;

        BlockIdxX = ctx.BlockIdxX;
        BlockIdxY = ctx.BlockIdxY;
        BlockIdxZ = ctx.BlockIdxZ;

        BlockDimX = ctx.BlockDimX;
        BlockDimY = ctx.BlockDimY;
        BlockDimZ = ctx.BlockDimZ;

        GridDimX = ctx.GridDimX;
        GridDimY = ctx.GridDimY;
        GridDimZ = ctx.GridDimZ;
    }

    /// <summary>
    /// Clears the thread context when execution is completed.
    /// </summary>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void ClearThreadContext() => IsActive = false;

    // Mocks for Warp Shuffle operations in Managed Execution

    private const int MaxThreadsPerBlock = 1024;
    private const int WarpExchangeSlotSize = 8; // Largest shuffled type (double / ulong)

    [ThreadStatic]
    private static byte[]? s_warpExchangeBuffer;

    [ThreadStatic]
    private static int[]? s_warpShuffleCounts;

    // Two planes alternate per shuffle: a lane that has already published its next shuffle writes the other plane, so it
    // never overwrites the value a lane scheduled after it in the same phase still has to read.
    private static Span<byte> GetWarpExchangeBuffer()
    {
        s_warpExchangeBuffer ??= new byte[2 * MaxThreadsPerBlock * WarpExchangeSlotSize];
        return s_warpExchangeBuffer;
    }

    private static int[] GetWarpShuffleCounts() => s_warpShuffleCounts ??= new int[MaxThreadsPerBlock];

    private static int GetWarpExchangeOffset(int plane, int linearTid) =>
        (plane * MaxThreadsPerBlock + linearTid) * WarpExchangeSlotSize;

    /// <summary>Returns the exchange plane of the current shuffle of the calling thread and advances its shuffle count.</summary>
    private static int TakeWarpExchangePlane(int linearTid) => GetWarpShuffleCounts()[linearTid]++ & 1;

    /// <summary>
    /// Resets the per-thread shuffle counts at the start of a block, so that every lane begins on the same exchange plane.
    /// </summary>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void ResetWarpExchange() => Array.Clear(GetWarpShuffleCounts(), 0, MaxThreadsPerBlock);

    /// <summary>
    /// Internal mock for warp exchange assignment. Do not use directly.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value to assign.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void __gsharp_warp_exchange_assign<T>(T value) where T : unmanaged
    {
        var linearTid = ThreadIdxZ * BlockDimY * BlockDimX + ThreadIdxY * BlockDimX + ThreadIdxX;
        var plane = GetWarpShuffleCounts()[linearTid] & 1;
        Unsafe.WriteUnaligned(ref GetWarpExchangeBuffer()[GetWarpExchangeOffset(plane, linearTid)], value);
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
    public static T __gsharp_warp_shuffle_read_down_sync<TContext, T>(TContext ctx, uint mask, T val, uint delta,
        int width = 32) where T : unmanaged
    {
        var linearTid = ThreadIdxZ * BlockDimY * BlockDimX + ThreadIdxY * BlockDimX + ThreadIdxX;
        var plane = TakeWarpExchangePlane(linearTid);
        var laneId = linearTid % 32;
        var warpStartTid = linearTid - laneId;

        var logicalLane = laneId % width;
        var targetLogicalLane = logicalLane + (int)delta;

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
        return Unsafe.ReadUnaligned<T>(ref GetWarpExchangeBuffer()[GetWarpExchangeOffset(plane, targetTid)]);

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
    public static T __gsharp_warp_shuffle_read_up_sync<TContext, T>(TContext ctx, uint mask, T val, uint delta,
        int width = 32) where T : unmanaged
    {
        var linearTid = ThreadIdxZ * BlockDimY * BlockDimX + ThreadIdxY * BlockDimX + ThreadIdxX;
        var plane = TakeWarpExchangePlane(linearTid);
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
        return Unsafe.ReadUnaligned<T>(ref GetWarpExchangeBuffer()[GetWarpExchangeOffset(plane, targetTid)]);
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
    public static T __gsharp_warp_shuffle_read_xor_sync<TContext, T>(TContext ctx, uint mask, T val, int delta,
        int width = 32) where T : unmanaged
    {
        var linearTid = ThreadIdxZ * BlockDimY * BlockDimX + ThreadIdxY * BlockDimX + ThreadIdxX;
        var plane = TakeWarpExchangePlane(linearTid);
        var laneId = linearTid % 32;
        var warpStartTid = linearTid - laneId;

        var logicalLane = laneId % width;
        var targetLogicalLane = logicalLane ^ delta;

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
        return Unsafe.ReadUnaligned<T>(ref GetWarpExchangeBuffer()[GetWarpExchangeOffset(plane, targetTid)]);
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
    public static T __gsharp_warp_shuffle_read_sync<TContext, T>(TContext ctx, uint mask, T val, int srcLane,
        int width = 32) where T : unmanaged
    {
        var linearTid = ThreadIdxZ * BlockDimY * BlockDimX + ThreadIdxY * BlockDimX + ThreadIdxX;
        var plane = TakeWarpExchangePlane(linearTid);
        var laneId = linearTid % 32;
        var warpStartTid = linearTid - laneId;

        var logicalLane = laneId % width;
        var targetLogicalLane = srcLane;

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
        return Unsafe.ReadUnaligned<T>(ref GetWarpExchangeBuffer()[GetWarpExchangeOffset(plane, targetTid)]);
    }
}
