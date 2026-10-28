using System.ComponentModel;
using System.Runtime.CompilerServices;

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
public static partial class ManagedGpuContext
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

    // Gpu.GridStrideLoop stops the start and the step here: no int index reaches 2^31, so index + step never wraps
    // around in 32 bits
    private const ulong GridStrideLimit = 0x80000000ul;

    /// <summary>
    /// The first index of the calling thread in <c>Gpu.GridStrideLoop</c>: its linear rank in the launch, in the order of
    /// cooperative groups <c>grid.thread_rank()</c> (x fastest, blocks in the same order), saturated at 2^31. The same
    /// computation as <c>__gsharp_grid_stride_start</c> of the C++ preludes. For generated code and the Core
    /// implementation only.
    /// </summary>
    /// <returns>The rank of the thread, or 2^31 when it is 2^31 or more.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static uint GridStrideStart()
    {
        // Below 2^63: 65535 * 65535 * (2^31 - 1), as cooperative groups num_blocks() notes
        ulong block = ((ulong)(uint)BlockIdxZ * (uint)GridDimY + (uint)BlockIdxY) * (uint)GridDimX + (uint)BlockIdxX;
        if (block >= GridStrideLimit)
        {
            return (uint)GridStrideLimit;
        }

        ulong rank = block * ((ulong)(uint)BlockDimX * (uint)BlockDimY * (uint)BlockDimZ)
            + ((ulong)(uint)ThreadIdxZ * (uint)BlockDimY + (uint)ThreadIdxY) * (uint)BlockDimX + (uint)ThreadIdxX;
        return rank >= GridStrideLimit ? (uint)GridStrideLimit : (uint)rank;
    }

    /// <summary>
    /// The step of <c>Gpu.GridStrideLoop</c>: the number of threads in the launch, saturated at 2^31. The same computation
    /// as <c>__gsharp_grid_stride_step</c> of the C++ preludes. For generated code and the Core implementation only.
    /// </summary>
    /// <returns>The number of threads, or 2^31 when it is 2^31 or more.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static uint GridStrideStep()
    {
        ulong blocks = (ulong)(uint)GridDimX * (uint)GridDimY * (uint)GridDimZ;
        if (blocks >= GridStrideLimit)
        {
            return (uint)GridStrideLimit;
        }

        ulong threads = blocks * ((ulong)(uint)BlockDimX * (uint)BlockDimY * (uint)BlockDimZ);
        return threads >= GridStrideLimit ? (uint)GridStrideLimit : (uint)threads;
    }

    /// <summary>
    /// The count of <c>Gpu.GridStrideLoop</c>, evaluated once: a negative count runs no iteration, as <c>i &lt; n</c> does
    /// in C#. The same as <c>__gsharp_grid_stride_count</c> of the C++ preludes. For generated code and the Core
    /// implementation only.
    /// </summary>
    /// <param name="count">The count argument of the loop.</param>
    /// <returns><paramref name="count"/>, or 0 when it is negative.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static uint GridStrideCount(int count) => count < 0 ? 0u : (uint)count;

    /// <summary>
    /// Set by generated code when the GPU thread being stepped stops at <c>Gpu.SyncThreads</c>, in the kernel or in any
    /// device function it calls; the managed launcher clears it before each step and reads it after, like
    /// <see cref="GridSyncRequested"/>. For generated code only.
    /// </summary>
    /// <remarks>
    /// The launcher keeps the thread waiting at the barrier until every thread of its block that has not finished waits
    /// too, as on the GPU, where no thread passes a block barrier before the others have reached it.
    /// </remarks>
    [ThreadStatic]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool BlockBarrierRequested;

    // The managed launcher of a cooperative kernel runs the grid in phases, each ending when every block has reached
    // Gpu.SyncGrid (#664). The GPU never completes a grid sync that a block, or thread 0 of a block, does not reach, and a
    // grid sync in a launch that is not cooperative is undefined there; the CPU paths report each with the same text.

    /// <summary>
    /// Creates the exception for a block that finished while the other blocks of a cooperative kernel wait at
    /// <c>Gpu.SyncGrid</c>. For generated code only.
    /// </summary>
    /// <param name="kernel">The kernel, as <c>Type.Method</c>.</param>
    /// <param name="blockIndex">The linear index of the block.</param>
    /// <param name="gridDimX">The number of blocks in X.</param>
    /// <param name="gridDimY">The number of blocks in Y.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static InvalidOperationException BlockFinishedAtGridSync(string kernel, int blockIndex, uint gridDimX, uint gridDimY)
    {
        var block = (uint)blockIndex;
        return new InvalidOperationException(
            $"G#: block ({block % gridDimX},{block / gridDimX % gridDimY},{block / (gridDimX * gridDimY)}) of {kernel} " +
            "finished while other blocks wait at Gpu.SyncGrid; on the GPU the grid sync never completes");
    }

    /// <summary>
    /// Creates the exception for thread 0 of a block that finished while the other threads of its block wait at
    /// <c>Gpu.SyncGrid</c>. For generated code only.
    /// </summary>
    /// <param name="kernel">The kernel, as <c>Type.Method</c>.</param>
    /// <param name="blockX">The block's index in X.</param>
    /// <param name="blockY">The block's index in Y.</param>
    /// <param name="blockZ">The block's index in Z.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static InvalidOperationException RepresentativeFinishedAtGridSync(string kernel, int blockX, int blockY, int blockZ) =>
        new($"G#: thread (0,0,0) of block ({blockX},{blockY},{blockZ}) of {kernel} finished while other threads of its " +
            "block wait at Gpu.SyncGrid; on the GPU the grid sync never completes");

    /// <summary>
    /// Creates the exception for <c>Gpu.SyncGrid</c> reached in a kernel that is not
    /// <c>[GpuKernel(Cooperative = true)]</c>. For generated code only.
    /// </summary>
    /// <param name="kernel">The kernel, as <c>Type.Method</c>.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static InvalidOperationException GridSyncInKernelThatIsNotCooperative(string kernel) =>
        new($"G#: Gpu.SyncGrid reached in {kernel}, which is not [GpuKernel(Cooperative = true)]");

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

    // How to avoid reading a lane that does not exist; a mask that leaves the lane out does not stop the read (the native
    // prelude, WarpAndHints.hpp, writes the same text)
    private const string MissingLaneHint =
        "(CUDA leaves the value undefined. Keep the source lane within the block by limiting delta, srcLane or laneMask, or launch a multiple of 32 threads; the mask does not limit which lane is read)";

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
                $"G#: {name} at {location} reads lane {srcLane} of warp {warpBase / 32}, which does not exist in a block of {threads} threads " +
                MissingLaneHint);
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
