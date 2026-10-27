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

    /// <summary>
    /// Set by generated code when the GPU thread being stepped reaches <c>Gpu.SyncGrid</c>, in the kernel or in any
    /// device function it calls; the managed launcher clears it before each step and reads it after. For generated code
    /// only.
    /// </summary>
    /// <remarks>
    /// The managed launcher steps one GPU thread at a time on one host thread, so a request set during a step belongs to
    /// that GPU thread, however deep the call that set it.
    /// </remarks>
    [ThreadStatic]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool GridSyncRequested;

    // One region per static shared memory declaration per block (a constant count, or a declaration in code that the
    // compiler does not rewrite). A later declaration never moves an earlier region: threads keep the span they were
    // handed, so moving a region would leave them writing to a copy nobody else reads. Only the same declaration reached
    // with a larger count moves its own region (see GrowRegion), which a constant count never does.
    // The thread-static state is kept between grids; only the region counts are reset.
    [ThreadStatic]
    private static SharedRegion?[]?[]? s_perBlockRegions;

    [ThreadStatic]
    private static int[]? s_perBlockRegionCount;

    private sealed class SharedRegion
    {
        public ulong SiteKey;
        public int TypeId;
        public int ByteLength;
        public byte[] Bytes = [];
    }

    // The dynamic shared memory of each block (#544): one array of the launch's sharedMemBytes, in which the dynamic
    // declarations get their regions with the arithmetic of the CUDA placement (__gsharp_dsmem_place in Prelude.cu) and
    // of the native CPU arena. The array of a block is allocated once per grid, so a span stays valid for the grid.
    [ThreadStatic]
    private static DynamicSharedMemory?[]? s_perBlockDynamic;

    [ThreadStatic]
    private static uint s_sharedMemBytes;

    private const ulong DynamicRegionAlignment = 16;

    private sealed class DynamicSharedMemory
    {
        public byte[] Bytes = [];
        public ulong Cursor;
        public int RegionCount;
        public DynamicRegion[] Regions = new DynamicRegion[8];
    }

    // A region is identified by the site key alone, as on CUDA and the native CPU path: the instantiations of a generic
    // device function share it
    private struct DynamicRegion
    {
        public ulong SiteKey;
        public ulong Offset;
        public ulong Size;
    }

    // CUDA gives every template instantiation of a device function its own __shared__ array, so the element type is part
    // of the identity of a declaration. The id is assigned once per closed type, without reflection.
    private static class SharedMemoryTypeIds
    {
        private static int s_next;

        public static int Next() => Interlocked.Increment(ref s_next);
    }

    private static class SharedMemoryTypeId<T> where T : unmanaged
    {
        public static readonly int Value = SharedMemoryTypeIds.Next();
    }

    private static int LinearBlockId => BlockIdxZ * GridDimY * GridDimX + BlockIdxY * GridDimX + BlockIdxX;

    /// <summary>
    /// Releases the dynamic shared memory of the blocks at the end of a grid; the next grid allocates its own.
    /// </summary>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void EndGrid() => s_perBlockDynamic = null;

    /// <summary>
    /// Initializes or resets the shared memory arrays for a grid without dynamic shared memory (a launch with
    /// <c>sharedMemBytes</c> 0).
    /// </summary>
    /// <param name="totalBlocks">The total number of blocks in the grid.</param>
    public static void InitializeGrid(int totalBlocks) => InitializeGrid(totalBlocks, 0);

    /// <summary>
    /// Initializes or resets the shared memory arrays for the entire grid execution.
    /// </summary>
    /// <param name="totalBlocks">The total number of blocks in the grid.</param>
    /// <param name="sharedMemBytes">
    /// The dynamic shared memory of each block, the <c>sharedMemBytes</c> of the launch, in which the dynamic declarations
    /// are placed.
    /// </param>
    public static void InitializeGrid(int totalBlocks, uint sharedMemBytes)
    {
        s_sharedMemBytes = sharedMemBytes;
        if (s_perBlockDynamic == null || s_perBlockDynamic.Length < totalBlocks)
        {
            Array.Resize(ref s_perBlockDynamic, totalBlocks);
        }

        for (var block = 0; block < totalBlocks; block++)
        {
            if (s_perBlockDynamic[block] is { } dynamic)
            {
                dynamic.Cursor = 0;
                dynamic.RegionCount = 0;
            }
        }

        if (s_perBlockRegions == null || s_perBlockRegionCount == null)
        {
            s_perBlockRegions = new SharedRegion?[]?[totalBlocks];
            s_perBlockRegionCount = new int[totalBlocks];
        }
        else
        {
            if (s_perBlockRegions.Length < totalBlocks)
            {
                Array.Resize(ref s_perBlockRegions, totalBlocks);
                Array.Resize(ref s_perBlockRegionCount, totalBlocks);
            }

            Array.Clear(s_perBlockRegionCount, 0, totalBlocks);
            // The regions themselves are reused by the next grid, so they are not cleared
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
    /// <exception cref="InvalidOperationException">The grid was not initialized.</exception>
    /// <remarks>
    /// [WHY] On the GPU a <c>__shared__</c> variable has static storage: one buffer per declaration per block, shared by
    /// every call of the function that declares it, from any call site and by every thread of the block. The buffer is
    /// therefore looked up by the declaration (its key and element type), not by the order of allocations, which differs
    /// between a caller and the device functions it calls and between threads that take different branches. A region
    /// does not move when other declarations are allocated, so the spans of earlier threads stay valid. When the same
    /// declaration is reached again with a larger <paramref name="count"/> (a device function called with a larger
    /// dynamic size), its region grows and keeps its contents, as a CUDA launch sized for the largest call would.
    /// </remarks>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static Span<T> AllocateSharedMemory<T>(ulong siteKey, int count) where T : unmanaged
    {
        if (count <= 0)
        {
            return [];
        }

        if (s_perBlockRegions == null || s_perBlockRegionCount == null)
        {
            throw new InvalidOperationException("Grid not initialized. Call InitializeGrid first.");
        }

        var byteCount = count * Unsafe.SizeOf<T>();
        var typeId = SharedMemoryTypeId<T>.Value;
        var blockId = LinearBlockId;
        var regions = s_perBlockRegions[blockId] ??= new SharedRegion?[16];
        var regionCount = s_perBlockRegionCount[blockId];

        for (var i = 0; i < regionCount; i++)
        {
            var region = regions[i];
            if (region == null || region.SiteKey != siteKey || region.TypeId != typeId)
            {
                continue;
            }

            if (byteCount > region.ByteLength)
            {
                GrowRegion(region, byteCount);
            }

            return MemoryMarshal.Cast<byte, T>(region.Bytes.AsSpan(0, byteCount));
        }

        if (regionCount == regions.Length)
        {
            Array.Resize(ref regions, regionCount * 2);
            s_perBlockRegions[blockId] = regions;
        }

        var created = regions[regionCount] ??= new SharedRegion();
        if (created.Bytes.Length < byteCount)
        {
            created.Bytes = new byte[byteCount];
        }

        created.SiteKey = siteKey;
        created.TypeId = typeId;
        created.ByteLength = byteCount;
        s_perBlockRegionCount[blockId] = regionCount + 1;

        return MemoryMarshal.Cast<byte, T>(created.Bytes.AsSpan(0, byteCount));
    }

    /// <summary>
    /// Returns the region of a dynamic <see cref="SharedMemory{T}"/> declaration (a count that is not a C# constant) in
    /// the current block's dynamic shared memory, placing it the first time the declaration is reached in the block.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type of the shared memory buffer.</typeparam>
    /// <param name="siteKey">The key that identifies the declaration. The G# compiler computes it for generated code.</param>
    /// <param name="count">The number of elements of the declaration.</param>
    /// <param name="place">The declaration in the C# source, for the error messages.</param>
    /// <returns>A span over the declaration's region, of <paramref name="count"/> elements.</returns>
    /// <exception cref="InvalidOperationException">
    /// The grid was not initialized, or the region grows after a later region was placed, which CUDA traps.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The regions do not fit in the launch's <c>sharedMemBytes</c> (a negative count included), which CUDA traps.
    /// </exception>
    /// <remarks>
    /// The rules are those of the CUDA placement and of the native CPU path (#544): regions are placed in the order the
    /// threads of the block first reach the declarations, each aligned to 16 bytes; a region that is still the last one
    /// grows in place; a growing region with a later region after it is an error, never a copy.
    /// </remarks>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static Span<T> AllocateDynamicSharedMemory<T>(ulong siteKey, int count, string place) where T : unmanaged
    {
        if (s_perBlockDynamic == null)
        {
            throw new InvalidOperationException("Grid not initialized. Call InitializeGrid first.");
        }

        var block = s_perBlockDynamic[LinearBlockId] ??= new DynamicSharedMemory();
        // A negative count arrives as at least 2^63 bytes, as on CUDA, so it fails the size check instead of wrapping
        var bytes = unchecked((ulong)(long)count * (ulong)Unsafe.SizeOf<T>());
        var index = FindDynamicRegion(block, siteKey);
        if (index < 0)
        {
            if (block.RegionCount == 0 && block.Bytes.Length < s_sharedMemBytes)
            {
                // The first region of the block in this grid: the array is allocated before any span into it is handed out
                block.Bytes = new byte[s_sharedMemBytes];
            }

            if (block.RegionCount == block.Regions.Length)
            {
                Array.Resize(ref block.Regions, block.RegionCount * 2);
            }

            index = block.RegionCount++;
            var offset = (block.Cursor + DynamicRegionAlignment - 1) & ~(DynamicRegionAlignment - 1);
            block.Regions[index] = new DynamicRegion { SiteKey = siteKey, Offset = offset, Size = 0 };
            // An empty region ends at its own offset and stays the last one until another region is placed
            block.Cursor = offset;
        }

        ref var region = ref block.Regions[index];
        if (bytes > region.Size)
        {
            if (region.Offset + region.Size != block.Cursor)
            {
                throw new InvalidOperationException($"G#: a shared memory declaration grew after a later declaration ({place})");
            }

            // [WHY] bytes is checked alone first: added to the offset, a negative count would wrap to a small end
            ulong limit = s_sharedMemBytes;
            if (bytes > limit || region.Offset + bytes > limit)
            {
                throw new ArgumentOutOfRangeException(null,
                    $"G#: dynamic shared memory exceeds sharedMemBytes ({place} needs {bytes} bytes at offset {region.Offset}; sharedMemBytes is {limit})");
            }

            region.Size = bytes;
            block.Cursor = region.Offset + bytes;
        }

        return MemoryMarshal.Cast<byte, T>(block.Bytes.AsSpan((int)region.Offset, (int)bytes));
    }

    /// <summary>
    /// Returns the region of a dynamic <see cref="SharedMemory{T}"/> declaration that the current block has already placed,
    /// or an empty span when the declaration was not reached yet; places nothing.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type of the shared memory buffer.</typeparam>
    /// <param name="siteKey">The key that identifies the declaration. The G# compiler computes it for generated code.</param>
    /// <returns>A span over the placed region (the size it was placed with), or an empty span.</returns>
    /// <remarks>
    /// [WHY] A state machine declares its <see cref="SharedMemory{T}"/> locals again at the start of every resume (a span
    /// cannot live in the context). That redeclaration only finds the region; the declaration itself places it, where the
    /// kernel reaches it, so the regions are placed in the order of the CUDA placement.
    /// </remarks>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static Span<T> FindDynamicSharedMemory<T>(ulong siteKey) where T : unmanaged
    {
        if (s_perBlockDynamic?[LinearBlockId] is not { } block)
        {
            return [];
        }

        var index = FindDynamicRegion(block, siteKey);
        if (index < 0)
        {
            return [];
        }

        var region = block.Regions[index];
        return MemoryMarshal.Cast<byte, T>(block.Bytes.AsSpan((int)region.Offset, (int)region.Size));
    }

    private static int FindDynamicRegion(DynamicSharedMemory block, ulong siteKey)
    {
        for (var i = 0; i < block.RegionCount; i++)
        {
            if (block.Regions[i].SiteKey == siteKey)
            {
                return i;
            }
        }

        return -1;
    }

    // [WHY] The generated code declares a SharedMemory<T> again on every resume and a span cannot live across a barrier
    // (GSH0039), so a span into the old array is not read once the region moves. A buffer kept from an earlier grid that
    // already fits is used in place.
    private static void GrowRegion(SharedRegion region, int byteCount)
    {
        if (region.Bytes.Length < byteCount)
        {
            var grown = new byte[byteCount];
            region.Bytes.AsSpan(0, region.ByteLength).CopyTo(grown);
            region.Bytes = grown;
        }

        region.ByteLength = byteCount;
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

    // The shuffles that CUDA leaves undefined throw InvalidOperationException with the place of the shuffle (#666): a width
    // that is not 1, 2, 4, 8, 16 or 32, and a source lane that does not exist in the block (the last warp of a block whose
    // size is not a multiple of 32). A source outside the caller's segment is defined: up and down return the caller's own
    // value, xor reads an earlier segment and returns the caller's own value for a later one, idx reads lane
    // srcLane % width of the segment (#692). The native CPU prelude (WarpAndHints.hpp) applies
    // the same rules.
    private static void CheckShuffleWidth(int width, string name, string location)
    {
        if (width is not (1 or 2 or 4 or 8 or 16 or 32))
        {
            throw new InvalidOperationException(
                $"G#: {name} at {location} has width {width}; the width must be 1, 2, 4, 8, 16 or 32 (CUDA leaves the result undefined)");
        }
    }

    private static int LinearThreadId => ThreadIdxZ * BlockDimY * BlockDimX + ThreadIdxY * BlockDimX + ThreadIdxX;

    // Reads the value that lane srcLane of the caller's warp published in the given plane, after checking that the lane
    // exists in the block
    private static T ReadShuffleLane<T>(int plane, int linearTid, int srcLane, string name, string location)
        where T : unmanaged
    {
        var threads = BlockDimX * BlockDimY * BlockDimZ;
        var warpBase = linearTid & ~31;
        var srcTid = warpBase + srcLane;
        if (srcTid >= threads)
        {
            throw new InvalidOperationException(
                $"G#: {name} at {location} reads lane {srcLane} of warp {warpBase / 32}, which does not exist in a block of {threads} threads (CUDA leaves the value undefined)");
        }

        return Unsafe.ReadUnaligned<T>(ref GetWarpExchangeBuffer()[GetWarpExchangeOffset(plane, srcTid)]);
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
    /// <param name="location">The place of the shuffle in the C# source, for the error message.</param>
    /// <returns>The shuffled value.</returns>
    /// <exception cref="InvalidOperationException">The width or the source lane is undefined on CUDA.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_shuffle_read_down_sync<TContext, T>(TContext ctx, uint mask, T val, uint delta,
        int width, string location) where T : unmanaged
    {
        const string Name = "Warp.ShuffleDownSync";
        var linearTid = LinearThreadId;
        var plane = TakeWarpExchangePlane(linearTid);
        CheckShuffleWidth(width, Name, location);
        var laneId = linearTid % 32;
        var logicalLane = laneId % width;
        var srcLogicalLane = (long)logicalLane + delta;
        return srcLogicalLane >= width
            ? val
            : ReadShuffleLane<T>(plane, linearTid, laneId - logicalLane + (int)srcLogicalLane, Name, location);
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
    /// <param name="location">The place of the shuffle in the C# source, for the error message.</param>
    /// <returns>The shuffled value.</returns>
    /// <exception cref="InvalidOperationException">The width or the source lane is undefined on CUDA.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_shuffle_read_up_sync<TContext, T>(TContext ctx, uint mask, T val, uint delta,
        int width, string location) where T : unmanaged
    {
        const string Name = "Warp.ShuffleUpSync";
        var linearTid = LinearThreadId;
        var plane = TakeWarpExchangePlane(linearTid);
        CheckShuffleWidth(width, Name, location);
        var laneId = linearTid % 32;
        var logicalLane = laneId % width;
        return logicalLane < delta
            ? val
            : ReadShuffleLane<T>(plane, linearTid, laneId - (int)delta, Name, location);
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
    /// <param name="location">The place of the shuffle in the C# source, for the error message.</param>
    /// <returns>The shuffled value.</returns>
    /// <exception cref="InvalidOperationException">The width or the source lane is undefined on CUDA.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_shuffle_read_xor_sync<TContext, T>(TContext ctx, uint mask, T val, int delta,
        int width, string location) where T : unmanaged
    {
        const string Name = "Warp.ShuffleXorSync";
        var linearTid = LinearThreadId;
        var plane = TakeWarpExchangePlane(linearTid);
        CheckShuffleWidth(width, Name, location);
        var laneId = (uint)(linearTid % 32);
        // CUDA lets a group of width lanes read its own group and the earlier ones; a later group gives the caller's own
        // value (PTX shfl.bfly reads lane j when j <= maxLane, the last lane of the caller's group)
        var srcLane = laneId ^ (uint)delta;
        return srcLane > (laneId | ((uint)width - 1u))
            ? val
            : ReadShuffleLane<T>(plane, linearTid, (int)srcLane, Name, location);
    }

    /// <summary>
    /// Internal mock for warp shuffle direct read. Do not use directly.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The thread mask.</param>
    /// <param name="val">The value to shuffle.</param>
    /// <param name="srcLane">The source lane ID; CUDA reads lane srcLane % width of the caller's segment.</param>
    /// <param name="width">The logical warp width.</param>
    /// <param name="location">The place of the shuffle in the C# source, for the error message.</param>
    /// <returns>The shuffled value.</returns>
    /// <exception cref="InvalidOperationException">The width or the source lane is undefined on CUDA.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_shuffle_read_sync<TContext, T>(TContext ctx, uint mask, T val, int srcLane,
        int width, string location) where T : unmanaged
    {
        const string Name = "Warp.ShuffleSync";
        var linearTid = LinearThreadId;
        var plane = TakeWarpExchangePlane(linearTid);
        CheckShuffleWidth(width, Name, location);
        var laneId = linearTid % 32;
        var logicalLane = laneId % width;
        return ReadShuffleLane<T>(plane, linearTid, laneId - logicalLane + (srcLane & (width - 1)), Name, location);
    }
}
