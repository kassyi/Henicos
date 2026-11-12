using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Core;

// The lanes of a warp meet at the same instance of a warp collective (#1468). Each lane publishes its value with the
// instance (the frame, the call and how often the frame reached it) and waits; the launcher runs it again when every
// lane of the mask that has not exited published the same instance in an earlier round, and reports lanes that never
// meet. The native CPU prelude (WarpAndHints.hpp) implements the same.
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

    // The thread-dependent branches and loops of methods that reach Warp.ActiveMask() that each thread of the block is
    // inside, innermost last (#844): WarpRegionCapacity entries per thread; and whether it waits where the innermost
    // one ends, since which round and where. Allocated by the first kernel that enters one
    [ThreadStatic]
    private static WarpInstance[]? s_warpRegions;

    [ThreadStatic]
    private static int[]? s_warpRegionDepth;

    [ThreadStatic]
    private static bool[]? s_warpReconverging;

    [ThreadStatic]
    private static int[]? s_warpReconvergeRound;

    [ThreadStatic]
    private static string?[]? s_warpReconvergeLocation;

    // The round in which the waiting lanes of each warp of the block were last decided, and whether each thread goes on
    [ThreadStatic]
    private static int[]? s_warpDecidedRound;

    [ThreadStatic]
    private static bool[]? s_warpMayPass;

    // The branches a thread can be inside at once (the native kernel context keeps as many)
    private const int WarpRegionCapacity = 16;

    private static WarpRecord[] WarpRecords => s_warpRecords ??= new WarpRecord[WarpRecordSlots * MaxThreadsPerBlock];

    private static int[] WarpRecordNext => s_warpRecordNext ??= new int[MaxThreadsPerBlock];

    private static bool[] WarpWaiting => s_warpWaiting ??= new bool[MaxThreadsPerBlock];

    private static bool[] WarpExited => s_warpExited ??= new bool[MaxThreadsPerBlock];

    /// <summary>
    /// Forgets the warp collectives of the threads at the start of a block. The launcher then marks the threads of the
    /// block that have exited (<see cref="MarkWarpLaneExited"/>).
    /// For generated code only.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void ResetWarpExchange()
    {
        Array.Clear(WarpRecords);
        Array.Clear(WarpRecordNext);
        Array.Clear(WarpWaiting);
        Array.Clear(WarpExited);
        s_warpRound = 0;
        if (s_warpRegionDepth != null)
        {
            Array.Clear(s_warpRegionDepth);
            Array.Clear(WarpReconverging);
            Array.Clear(WarpDecidedRound);
        }
    }

    /// <summary>
    /// Records that a thread of the block has exited: it no longer takes part in a warp collective.
    /// For generated code only.
    /// </summary>
    /// <param name="threadIndex">The linear index of the thread in its block.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void MarkWarpLaneExited(int threadIndex) => WarpExited[threadIndex] = true;

    /// <summary>
    /// Whether a thread of the block takes its next step in this round: it does not wait at a warp collective, or every
    /// lane of the mask that has not exited published the same instance in an earlier round. The lanes of a group all
    /// go on in the same round, so none of them publishes twice before the others have read.
    /// For generated code only.
    /// </summary>
    /// <param name="threadIndex">The linear index of the thread in its block.</param>
    /// <param name="round">The launcher's round of the block, counted from 1.</param>
    /// <returns>Whether the thread runs in this round.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static bool WarpCanRun(int threadIndex, int round)
    {
        s_warpRound = round;
        if (s_warpReconverging != null)
        {
            // A lane that waits where a branch ends goes on once the lanes that entered it with it have arrived (#844)
            DecideReconvergence(threadIndex, round);
            if (s_warpReconverging[threadIndex])
            {
                return WarpMayPass[threadIndex];
            }
        }

        return !WarpWaiting[threadIndex] || GetMissingLanes(threadIndex, round) == 0u;
    }

    /// <summary>
    /// The error of a round in which no thread of the block could run while some wait at a warp collective: a lane of
    /// their mask that has not exited never reaches the same call with them, which CUDA leaves undefined.
    /// For generated code only.
    /// </summary>
    /// <param name="threadsPerBlock">The threads of the block.</param>
    /// <returns>The exception to throw, which names the first such lane.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
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
            var where = s_warpReconverging != null && s_warpReconverging[other]
                ? $"waits for the other lanes where the branch or call at {WarpReconvergeLocation[other]} ends"
                : WarpWaiting[other]
                    ? $"waits at {WarpRecords[GetWaitSlot(other)].Name} at {WarpRecords[GetWaitSlot(other)].Location}"
                    : "waits at a barrier or has not reached it";
            return new InvalidOperationException(
                $"G#: {wait.Name} at {wait.Location} in lane {tid & 31} of warp {tid / 32} " +
                $"waits for lanes 0x{missing:X8} of its mask 0x{wait.Mask:X8}, " +
                $"which never reach the same call: lane {first} {where} " +
                "(CUDA leaves the result undefined unless every lane of the mask that has not exited " +
                "calls it with the same mask)");
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
    /// <exception cref="InvalidOperationException">
    /// The mask does not contain the caller's lane, or the lanes pass different masks.
    /// </exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void __gsharp_warp_sync_read<TFrame>(int root, ref TFrame frame, int site, uint mask, string location)
        where TFrame : IManagedWarpFrame =>
        GetVoteLanes(LinearThreadId, BeginWarpRead(ref frame, site), mask, "Gpu.SyncWarp", location);

    /// <summary>
    /// Internal: <c>Warp.ActiveMask()</c> (#844): the lanes of the caller's warp that stopped at the same call in the
    /// same round, which the lanes that reach it together on the GPU do. Do not use directly.
    /// </summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <param name="root">Unused: the managed path keeps the published values itself.</param>
    /// <param name="frame">The frame the call is in.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>
    /// The lanes that took the same way through the thread-dependent branches since they last ran together and have not
    /// exited (a lane that has exited since it stopped at the call still counts).
    /// </returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static uint __gsharp_warp_active_mask_read<TFrame>(int root, ref TFrame frame, int site, string location)
        where TFrame : IManagedWarpFrame
    {
        var tid = LinearThreadId;
        var instance = BeginWarpRead(ref frame, site);
        var own = FindWarpRecord(tid, instance);
        if (own < 0)
        {
            throw new InvalidOperationException(
                $"G#: internal error: Warp.ActiveMask() at {location} finds no record of its own call " +
                "(please report this G# bug)");
        }

        var round = WarpRecords[own].Round;
        var threads = BlockDimX * BlockDimY * BlockDimZ;
        var warpBase = tid & ~31;
        var lanes = 0u;
        for (var lane = 0; lane < 32 && warpBase + lane < threads; lane++)
        {
            for (var i = 0; i < WarpRecordSlots; i++)
            {
                ref readonly var record = ref WarpRecords[WarpRecordSlots * (warpBase + lane) + i];
                if (record.Visit != 0u && record.Round == round && record.Site == instance.Site &&
                    record.Path == instance.Path)
                {
                    lanes |= 1u << lane;
                    break;
                }
            }
        }

        return lanes;
    }

    /// <summary>
    /// Internal: the calling lane enters a thread-dependent branch or loop of a method that reaches
    /// <c>Warp.ActiveMask()</c> (#844). Do not use directly.
    /// </summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <param name="root">Unused.</param>
    /// <param name="frame">The frame the branch is in, which counts its entries.</param>
    /// <param name="site">The number of the branch in the body of its function.</param>
    /// <exception cref="InvalidOperationException">The lane is inside too many branches at once.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void __gsharp_warp_region_enter<TFrame>(int root, ref TFrame frame, int site)
        where TFrame : IManagedWarpFrame
    {
        var tid = LinearThreadId;
        var depth = WarpRegionDepth[tid];
        if (depth == WarpRegionCapacity)
        {
            throw new InvalidOperationException(
                $"G#: a thread is inside more than {WarpRegionCapacity} thread-dependent branches and loops at once " +
                "in methods that reach Warp.ActiveMask(); the CPU paths cannot follow which lanes run together");
        }

        WarpRegions[WarpRegionCapacity * tid + depth] =
            new WarpInstance(frame.WarpPath, site, frame.AdvanceWarpVisit(site));
        WarpRegionDepth[tid] = depth + 1;
    }

    /// <summary>Internal: the calling lane leaves a branch by a jump out of it (#844). Do not use directly.</summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <param name="root">Unused.</param>
    /// <param name="frame">The frame the branch is in.</param>
    /// <param name="site">The number of the branch in the body of its function.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void __gsharp_warp_region_leave<TFrame>(int root, ref TFrame frame, int site)
        where TFrame : IManagedWarpFrame
    {
        var tid = LinearThreadId;
        var depth = WarpRegionDepth[tid];
        var top = depth > 0 ? WarpRegions[WarpRegionCapacity * tid + depth - 1] : default;
        if (depth == 0 || top.Site != site || top.Path != frame.WarpPath)
        {
            throw new InvalidOperationException(
                "G#: internal error: a thread left a branch it is not inside (please report this G# bug)");
        }

        WarpRegionDepth[tid] = depth - 1;
    }

    /// <summary>
    /// Internal: the calling lane arrives where the innermost branch it is inside ends and waits for the other lanes
    /// that entered it (#844). Do not use directly.
    /// </summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <param name="root">Unused.</param>
    /// <param name="frame">The frame the branch is in.</param>
    /// <param name="site">The number of the branch in the body of its function.</param>
    /// <param name="location">The place of the branch in the C# source, for the error message of a stall.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void __gsharp_warp_reconverge_arrive<TFrame>(int root, ref TFrame frame, int site, string location)
        where TFrame : IManagedWarpFrame
    {
        var tid = LinearThreadId;
        WarpReconverging[tid] = true;
        WarpMayPass[tid] = false;
        WarpReconvergeRound[tid] = s_warpRound;
        WarpReconvergeLocation[tid] = location;
    }

    /// <summary>
    /// Internal: the lanes that entered the branch with the calling lane have arrived or left; it leaves the branch
    /// (#844). Do not use directly.
    /// </summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <param name="root">Unused.</param>
    /// <param name="frame">The frame the branch is in.</param>
    /// <param name="site">The number of the branch in the body of its function.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void __gsharp_warp_reconverge_pass<TFrame>(int root, ref TFrame frame, int site)
        where TFrame : IManagedWarpFrame
    {
        WarpReconverging[LinearThreadId] = false;
        __gsharp_warp_region_leave(root, ref frame, site);
    }

    private static WarpInstance[] WarpRegions =>
        s_warpRegions ??= new WarpInstance[WarpRegionCapacity * MaxThreadsPerBlock];

    private static int[] WarpRegionDepth => s_warpRegionDepth ??= new int[MaxThreadsPerBlock];

    private static bool[] WarpReconverging => s_warpReconverging ??= new bool[MaxThreadsPerBlock];

    private static int[] WarpReconvergeRound => s_warpReconvergeRound ??= new int[MaxThreadsPerBlock];

    private static string?[] WarpReconvergeLocation => s_warpReconvergeLocation ??= new string?[MaxThreadsPerBlock];

    private static int[] WarpDecidedRound => s_warpDecidedRound ??= new int[MaxThreadsPerBlock / 32];

    private static bool[] WarpMayPass => s_warpMayPass ??= new bool[MaxThreadsPerBlock];

    // [WHY] Which waiting lanes of a warp go on is decided once per round, before any lane of the warp takes its step
    // (the first call for the warp in the round): a lane that leaves the branch by a jump earlier in the same round
    // must still hold back the lanes after it, so that every lane of the entry goes on in the same round (the native
    // prelude does the same)
    private static void DecideReconvergence(int threadIndex, int round)
    {
        var warp = threadIndex >> 5;
        if (WarpDecidedRound[warp] == round)
        {
            return;
        }

        WarpDecidedRound[warp] = round;
        var threads = BlockDimX * BlockDimY * BlockDimZ;
        var reconverging = WarpReconverging;
        for (var laneTid = warp * 32; laneTid < warp * 32 + 32 && laneTid < threads; laneTid++)
        {
            if (!WarpExited[laneTid] && reconverging[laneTid])
            {
                WarpMayPass[laneTid] = GetMissingReconvergeLanes(laneTid, round) == 0u;
            }
        }
    }

    // The other lanes of the warp of tid that have not exited and are inside the entry of the branch where tid waits,
    // and have not arrived at its end before round. A lane that arrived in this round still counts: the lanes of an
    // entry all go on in the same round (the native prelude computes the same)
    private static uint GetMissingReconvergeLanes(int tid, int round)
    {
        var region = WarpRegions[WarpRegionCapacity * tid + WarpRegionDepth[tid] - 1];
        var threads = BlockDimX * BlockDimY * BlockDimZ;
        var warpBase = tid & ~31;
        var missing = 0u;
        for (var lane = 0; lane < 32; lane++)
        {
            var laneTid = warpBase + lane;
            if (laneTid >= threads || laneTid == tid || WarpExited[laneTid])
            {
                continue;
            }

            var depth = WarpRegionDepth[laneTid];
            for (var d = 0; d < depth; d++)
            {
                if (WarpRegions[WarpRegionCapacity * laneTid + d] != region)
                {
                    continue;
                }

                var arrived = WarpReconverging[laneTid] && d + 1 == depth && WarpReconvergeRound[laneTid] < round;
                if (!arrived)
                {
                    missing |= 1u << lane;
                }

                break;
            }
        }

        return missing;
    }

    // The slot of the value that thread tid waits with: the one it published last
    private static int GetWaitSlot(int tid) => WarpRecordSlots * tid + (WarpRecordNext[tid] ^ 1);

    // The lanes of the mask that tid waits with that have not exited and have not published the same instance before
    // round
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

    // The slot in which thread tid published the instance, or -1 (a visit count starts at 1, so an empty slot never
    // matches)
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

    // Reads the value that lane srcLane of the caller's warp published for the same instance of the call. Reading a
    // lane that does not exist in the block, that has exited or that does not take part, or one that called it with
    // another mask, is undefined on CUDA (the native prelude writes the same text)
    private static T ReadShuffleLane<T>(int linearTid, in WarpInstance instance, uint mask, int srcLane, string name,
        string location) where T : unmanaged
    {
        var threads = BlockDimX * BlockDimY * BlockDimZ;
        var warpBase = linearTid & ~31;
        var srcTid = warpBase + srcLane;
        if (srcTid >= threads)
        {
            throw new InvalidOperationException(
                $"G#: {name} at {location} reads lane {srcLane} of warp {warpBase / 32}, " +
                $"which does not exist in a block of {threads} threads " +
                MissingLaneHint);
        }

        var slot = FindWarpRecord(srcTid, instance);
        if (slot < 0)
        {
            throw new InvalidOperationException(WarpExited[srcTid]
                ? $"G#: {name} at {location} in lane {linearTid & 31} reads lane {srcLane} of warp {warpBase / 32}, " +
                  "which has exited (CUDA leaves the value of a lane that does not take part undefined)"
                : $"G#: {name} at {location} in lane {linearTid & 31} reads lane {srcLane} of warp {warpBase / 32}, " +
                  $"which does not take part: it is not in the mask 0x{mask:X8} and has not reached the same call " +
                  "(CUDA leaves the value of a lane that does not take part undefined)");
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
        new($"G#: {name} at {location} has mask 0x{mask:X8} in lane {lane} and mask 0x{otherMask:X8} " +
            $"in lane {otherLane} of warp {warp} " +
            "(CUDA leaves the result undefined unless the lanes call it with the same mask)");

    // The lanes that take part in a vote, reduction, match or Gpu.SyncWarp: the lanes of the mask that reached the same
    // instance of the call. A lane of the mask that has exited does not take part, as CUDA evaluates the call for the
    // lanes of the mask that have not exited. The caller must be in the mask and every lane must pass the same mask
    private static uint GetVoteLanes(int linearTid, in WarpInstance instance, uint mask, string name, string location)
    {
        var laneId = linearTid & 31;
        if ((mask & (1u << laneId)) == 0u)
        {
            throw new InvalidOperationException(
                $"G#: {name} at {location} has mask 0x{mask:X8} without caller lane {laneId} " +
                "(CUDA leaves the result undefined)");
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
