using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Core;

// The shared memory of the managed execution context: the static regions of each block and the dynamic regions placed in
// the launch's sharedMemBytes (#544)
public static partial class ManagedGpuContext
{
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
    // of the native CPU arena. When the blocks run in grid phases (a cooperative kernel), every block keeps its array for
    // the grid, so a span stays valid across the grid syncs. Otherwise each block runs to its end before the next one
    // starts, and the blocks use one array in turn, as the native arena of an OpenMP thread does.
    [ThreadStatic]
    private static DynamicSharedMemory?[]? s_perBlockDynamic;

    [ThreadStatic]
    private static bool s_blocksRunInPhases;

    // The block that owns the one array of blocks that do not run in phases, or -1
    [ThreadStatic]
    private static int s_dynamicOwner;

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

    [ThreadStatic]
    private static ulong s_currentTypeKeySalt;

    internal static ulong CurrentTypeKeySalt => s_currentTypeKeySalt;

    // A generic struct constructor is monomorphized per closed struct type on CUDA and on the native CPU path (#543), so
    // each closed generic struct gets a distinct 64-bit salt on the managed path without reflection.
    private static class TypeKeySalt<TKey>
    {
        public static readonly ulong Value = unchecked((ulong)SharedMemoryTypeIds.Next() * 0x9E3779B97F4A7C15UL);
    }

    /// <summary>
    /// Enters a generic struct constructor scope for managed shared memory site-key disambiguation (#543). For generated
    /// code only.
    /// </summary>
    /// <typeparam name="TKey">The closed generic struct type being constructed.</typeparam>
    /// <returns>The previous type-key salt to pass to <see cref="EndTypeKeyScope{T}"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ulong BeginTypeKeyScope<TKey>()
    {
        var previous = s_currentTypeKeySalt;
        s_currentTypeKeySalt = previous ^ TypeKeySalt<TKey>.Value;
        return previous;
    }

    /// <summary>
    /// Restores the previous type-key salt after a generic struct constructor completes (#543). For generated code only.
    /// </summary>
    /// <typeparam name="T">The constructed struct type.</typeparam>
    /// <param name="previousSalt">The salt returned by <see cref="BeginTypeKeyScope{TKey}"/>.</param>
    /// <param name="value">The constructed struct value.</param>
    /// <returns><paramref name="value"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T EndTypeKeyScope<T>(ulong previousSalt, T value)
    {
        s_currentTypeKeySalt = previousSalt;
        return value;
    }

    private static int LinearBlockId => BlockIdxZ * GridDimY * GridDimX + BlockIdxY * GridDimX + BlockIdxX;

    // The index of the current block's dynamic shared memory. Blocks that do not run in phases share index 0: the first
    // use by a block empties the array of the block before it, which has finished
    private static int CurrentDynamicIndex()
    {
        if (s_blocksRunInPhases)
        {
            return LinearBlockId;
        }

        if (s_dynamicOwner != LinearBlockId && s_perBlockDynamic is { Length: > 0 } && s_perBlockDynamic[0] is { } shared)
        {
            shared.Cursor = 0;
            shared.RegionCount = 0;
        }

        s_dynamicOwner = LinearBlockId;
        return 0;
    }

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
    /// Initializes or resets the shared memory arrays for the entire grid execution, keeping the dynamic shared memory of
    /// every block for the grid.
    /// </summary>
    /// <param name="totalBlocks">The total number of blocks in the grid.</param>
    /// <param name="sharedMemBytes">
    /// The dynamic shared memory of each block, the <c>sharedMemBytes</c> of the launch, in which the dynamic declarations
    /// are placed.
    /// </param>
    public static void InitializeGrid(int totalBlocks, uint sharedMemBytes) =>
        InitializeGrid(totalBlocks, sharedMemBytes, blocksRunInPhases: true);

    /// <summary>
    /// Initializes or resets the shared memory arrays for the entire grid execution.
    /// </summary>
    /// <param name="totalBlocks">The total number of blocks in the grid.</param>
    /// <param name="sharedMemBytes">
    /// The dynamic shared memory of each block, the <c>sharedMemBytes</c> of the launch, in which the dynamic declarations
    /// are placed.
    /// </param>
    /// <param name="blocksRunInPhases">
    /// Whether the blocks run in grid phases (a cooperative kernel), so that every block keeps its dynamic shared memory
    /// for the grid; otherwise each block runs to its end, and the blocks use one array in turn.
    /// </param>
    public static void InitializeGrid(int totalBlocks, uint sharedMemBytes, bool blocksRunInPhases)
    {
        s_currentTypeKeySalt = 0;
        s_sharedMemBytes = sharedMemBytes;
        s_blocksRunInPhases = blocksRunInPhases;
        s_dynamicOwner = -1;
        var dynamicArrays = blocksRunInPhases ? totalBlocks : Math.Min(totalBlocks, 1);
        if (s_perBlockDynamic == null || s_perBlockDynamic.Length < dynamicArrays)
        {
            Array.Resize(ref s_perBlockDynamic, dynamicArrays);
        }

        for (var block = 0; block < dynamicArrays; block++)
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

        var block = s_perBlockDynamic[CurrentDynamicIndex()] ??= new DynamicSharedMemory();
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
        if (s_perBlockDynamic?[CurrentDynamicIndex()] is not { } block)
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
}
