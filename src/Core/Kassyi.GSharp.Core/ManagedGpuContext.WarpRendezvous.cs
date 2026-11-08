using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Core;

// The lanes of a warp meet at the same instance of a warp collective (#1468). Each lane publishes its value with the
// instance (the frame, the call and how often the frame reached it) and waits; the launcher runs it again when every lane
// of the mask that has not exited published the same instance in an earlier round, and reports lanes that never meet.
// The native CPU prelude (WarpAndHints.hpp) implements the same.
public static partial class ManagedGpuContext
{
    private struct WarpRecord
    {
        public ulong Value;
        public uint Path;
        public int Site;
        public uint Visit;
        public int Round;
        public uint Mask;
        public string? Name;
        public string? Location;
    }

    private readonly record struct WarpInstance(uint Path, int Site, uint Visit);

    private const int WarpRecordSlots = 2;

    // The last two values each thread of the block published, which slot it writes next, whether it waits at a warp
    // collective and whether it has exited
    [ThreadStatic]
    private static WarpRecord[]? s_warpRecords;

    [ThreadStatic]
    private static int[]? s_warpRecordNext;

    [ThreadStatic]
    private static bool[]? s_warpWaiting;

    [ThreadStatic]
    private static bool[]? s_warpExited;

    // The launcher's round of the block
    [ThreadStatic]
    private static int s_warpRound;

    private static WarpRecord[] WarpRecords => s_warpRecords ??= new WarpRecord[WarpRecordSlots * MaxThreadsPerBlock];

    private static int[] WarpRecordNext => s_warpRecordNext ??= new int[MaxThreadsPerBlock];

    private static bool[] WarpWaiting => s_warpWaiting ??= new bool[MaxThreadsPerBlock];

    private static bool[] WarpExited => s_warpExited ??= new bool[MaxThreadsPerBlock];

    /// <summary>
    /// Forgets the warp collectives of the threads at the start of a block. The launcher then marks the threads of the
    /// block that have exited (<see cref="MarkWarpLaneExited"/>).
    /// </summary>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void ResetWarpExchange()
    {
        Array.Clear(WarpRecords);
        Array.Clear(WarpRecordNext);
        Array.Clear(WarpWaiting);
        Array.Clear(WarpExited);
        s_warpRound = 0;
    }

    /// <summary>Records that a thread of the block has exited: it no longer takes part in a warp collective.</summary>
    /// <param name="threadIndex">The linear index of the thread in its block.</param>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void MarkWarpLaneExited(int threadIndex) => WarpExited[threadIndex] = true;

    /// <summary>
    /// Whether a thread of the block takes its next step in this round: it does not wait at a warp collective, or every
    /// lane of the mask that has not exited published the same instance in an earlier round. The lanes of a group all
    /// go on in the same round, so none of them publishes twice before the others have read.
    /// </summary>
    /// <param name="threadIndex">The linear index of the thread in its block.</param>
    /// <param name="round">The launcher's round of the block, counted from 1.</param>
    /// <returns>Whether the thread runs in this round.</returns>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static bool WarpCanRun(int threadIndex, int round)
    {
        s_warpRound = round;
        return !WarpWaiting[threadIndex] || GetMissingLanes(threadIndex, round) == 0u;
    }

    /// <summary>
    /// The error of a round in which no thread of the block could run while some wait at a warp collective: a lane of
    /// their mask that has not exited never reaches the same call with them, which CUDA leaves undefined.
    /// </summary>
    /// <param name="threadsPerBlock">The threads of the block.</param>
    /// <returns>The exception to throw, which names the first such lane.</returns>
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static InvalidOperationException WarpStall(int threadsPerBlock)
    {
        for (var tid = 0; tid < threadsPerBlock; tid++)
        {
            if (WarpExited[tid] || !WarpWaiting[tid])
            {
                continue;
            }

            var missing = GetMissingLanes(tid, int.MaxValue);
            if (missing == 0u)
            {
                continue;
            }

            var wait = WarpRecords[GetWaitSlot(tid)];
            var first = BitOperations.TrailingZeroCount(missing);
            var other = (tid & ~31) + first;
            var where = WarpWaiting[other]
                ? $"waits at {WarpRecords[GetWaitSlot(other)].Name} at {WarpRecords[GetWaitSlot(other)].Location}"
                : "waits at a barrier or has not reached it";
            return new InvalidOperationException(
                $"G#: {wait.Name} at {wait.Location} in lane {tid & 31} of warp {tid / 32} waits for lanes 0x{missing:X8} of its mask 0x{wait.Mask:X8}, " +
                $"which never reach the same call: lane {first} {where} (CUDA leaves the result undefined unless every lane of the mask that has not exited calls it with the same mask)");
        }

        return new InvalidOperationException(
            "G#: the threads of a block wait for each other at warp collectives that never complete");
    }

    /// <summary>Internal: publishes the value of the calling lane at a warp collective. Do not use directly.</summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="root">Unused: the managed path keeps the published values itself.</param>
    /// <param name="frame">The frame the call is in.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="value">The value of the lane (0 for <c>Gpu.SyncWarp</c>).</param>
    /// <param name="name">The C# name of the call, for the error messages.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void __gsharp_warp_publish<TFrame, T>(int root, ref TFrame frame, int site, uint mask, T value,
        string name, string location) where TFrame : IManagedWarpFrame where T : unmanaged
    {
        var tid = LinearThreadId;
        var slot = WarpRecordSlots * tid + WarpRecordNext[tid];
        WarpRecordNext[tid] ^= 1;
        ulong raw = 0;
        Unsafe.WriteUnaligned(ref Unsafe.As<ulong, byte>(ref raw), value);
        WarpRecords[slot] = new WarpRecord
        {
            Value = raw,
            Path = frame.WarpPath,
            Site = site,
            Visit = frame.AdvanceWarpVisit(site),
            Round = s_warpRound,
            Mask = mask,
            Name = name,
            Location = location
        };
        WarpWaiting[tid] = true;
    }

    /// <summary>Internal mock for <c>Gpu.SyncWarp</c>: the lanes of the mask meet. Do not use directly.</summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <param name="root">Unused: the managed path keeps the published values itself.</param>
    /// <param name="frame">The frame the call is in.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane, or the lanes pass different masks.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void __gsharp_warp_sync_read<TFrame>(int root, ref TFrame frame, int site, uint mask, string location)
        where TFrame : IManagedWarpFrame =>
        GetVoteLanes(LinearThreadId, BeginWarpRead(ref frame, site), mask, "Gpu.SyncWarp", location);

    // The slot of the value that thread tid waits with: the one it published last
    private static int GetWaitSlot(int tid) => WarpRecordSlots * tid + (WarpRecordNext[tid] ^ 1);

    // The lanes of the mask that tid waits with that have not exited and have not published the same instance before round
    private static uint GetMissingLanes(int tid, int round)
    {
        var wait = WarpRecords[GetWaitSlot(tid)];
        var instance = new WarpInstance(wait.Path, wait.Site, wait.Visit);
        var threads = BlockDimX * BlockDimY * BlockDimZ;
        var warpBase = tid & ~31;
        var missing = 0u;
        for (var lane = 0; lane < 32; lane++)
        {
            var laneTid = warpBase + lane;
            if ((wait.Mask & (1u << lane)) == 0u || laneTid >= threads || WarpExited[laneTid])
            {
                continue;
            }

            var slot = FindWarpRecord(laneTid, instance);
            if (slot < 0 || WarpRecords[slot].Round >= round)
            {
                missing |= 1u << lane;
            }
        }

        return missing;
    }

    // The slot in which thread tid published the instance, or -1 (a visit count starts at 1, so an empty slot never matches)
    private static int FindWarpRecord(int tid, in WarpInstance instance)
    {
        for (var i = 0; i < WarpRecordSlots; i++)
        {
            var slot = WarpRecordSlots * tid + i;
            ref readonly var record = ref WarpRecords[slot];
            if (record.Visit == instance.Visit && record.Site == instance.Site && record.Path == instance.Path)
            {
                return slot;
            }
        }

        return -1;
    }

    // The start of a read: the lane no longer waits, and the instance it reads is the one it published
    private static WarpInstance BeginWarpRead<TFrame>(ref TFrame frame, int site) where TFrame : IManagedWarpFrame
    {
        WarpWaiting[LinearThreadId] = false;
        return new WarpInstance(frame.WarpPath, site, frame.GetWarpVisit(site));
    }

    // Reads the value that lane srcLane of the caller's warp published for the same instance of the call. Reading a lane
    // that does not exist in the block, that has exited or that does not take part, or one that called it with another
    // mask, is undefined on CUDA (the native prelude writes the same text)
    private static T ReadShuffleLane<T>(int linearTid, in WarpInstance instance, uint mask, int srcLane, string name,
        string location) where T : unmanaged
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

        var slot = FindWarpRecord(srcTid, instance);
        if (slot < 0)
        {
            throw new InvalidOperationException(WarpExited[srcTid]
                ? $"G#: {name} at {location} in lane {linearTid & 31} reads lane {srcLane} of warp {warpBase / 32}, which has exited (CUDA leaves the value of a lane that does not take part undefined)"
                : $"G#: {name} at {location} in lane {linearTid & 31} reads lane {srcLane} of warp {warpBase / 32}, which does not take part: it is not in the mask 0x{mask:X8} and has not reached the same call (CUDA leaves the value of a lane that does not take part undefined)");
        }

        var record = WarpRecords[slot];
        if (record.Mask != mask)
        {
            throw MaskMismatch(name, location, mask, linearTid & 31, record.Mask, srcLane, warpBase / 32);
        }

        var raw = record.Value;
        return Unsafe.ReadUnaligned<T>(ref Unsafe.As<ulong, byte>(ref raw));
    }

    private static InvalidOperationException MaskMismatch(string name, string location, uint mask, int lane,
        uint otherMask, int otherLane, int warp) =>
        new($"G#: {name} at {location} has mask 0x{mask:X8} in lane {lane} and mask 0x{otherMask:X8} in lane {otherLane} of warp {warp} (CUDA leaves the result undefined unless the lanes call it with the same mask)");

    // The lanes that take part in a vote, reduction, match or Gpu.SyncWarp: the lanes of the mask that reached the same
    // instance of the call. A lane of the mask that has exited does not take part, as CUDA evaluates the call for the
    // lanes of the mask that have not exited. The caller must be in the mask and every lane must pass the same mask
    private static uint GetVoteLanes(int linearTid, in WarpInstance instance, uint mask, string name, string location)
    {
        var laneId = linearTid & 31;
        if ((mask & (1u << laneId)) == 0u)
        {
            throw new InvalidOperationException(
                $"G#: {name} at {location} has mask 0x{mask:X8} without caller lane {laneId} (CUDA leaves the result undefined)");
        }

        var threads = BlockDimX * BlockDimY * BlockDimZ;
        var warpBase = linearTid & ~31;
        var lanes = 0u;
        for (var lane = 0; lane < 32; lane++)
        {
            if ((mask & (1u << lane)) == 0u || warpBase + lane >= threads)
            {
                continue;
            }

            var slot = FindWarpRecord(warpBase + lane, instance);
            if (slot < 0)
            {
                continue;
            }

            if (WarpRecords[slot].Mask != mask)
            {
                throw MaskMismatch(name, location, mask, laneId, WarpRecords[slot].Mask, lane, warpBase / 32);
            }

            lanes |= 1u << lane;
        }

        return lanes;
    }
}
