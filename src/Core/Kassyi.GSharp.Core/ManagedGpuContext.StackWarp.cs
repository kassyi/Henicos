using System.ComponentModel;
using System.Numerics;

namespace Kassyi.GSharp.Core;

// The warp collectives of the managed stack model. Each collective is one call: the lane publishes its value with the
// instance of the call (the same records as the state machine, ManagedGpuContext.WarpRendezvous.cs), gives the baton to
// the scheduler, and reads once the scheduler finds the lanes of its mask met there (WarpCanRun). The instance is the
// same as in the state machine: the path of a frame is its offset in the visit counts of the GPU thread, fixed at
// compile time per static call path, and a count adds up over every call through the same path.
public static partial class ManagedGpuContext
{
    /// <summary>
    /// Internal: clears the visit counts of the warp collectives at the start of a kernel of the managed stack model.
    /// Do not use directly.
    /// </summary>
    /// <param name="size">The counts of the kernel: the size of its frame and of the frames of its calls.</param>
    /// <exception cref="InvalidOperationException">Called outside a launch of the managed stack model.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void __gsharp_stack_warp_begin(int size)
    {
        var self = StackWarpSelf();
        if (size <= 0)
        {
            throw new InvalidOperationException(
                $"G#: internal error: a kernel of the stack model has {size} warp visit counts " +
                "(please report this G# bug)");
        }

        if (self.WarpVisits.Length < size)
        {
            // [WHY] Grown in powers of two and kept by the pooled OS thread: the later GPU threads and launches that the
            // thread runs reuse it, so a launch allocates nothing once the threads have grown to its kernel
            var length = size > 1 << 30 ? size : Math.Max(64, (int)BitOperations.RoundUpToPowerOf2((uint)size));
            self.WarpVisits = new uint[length];
        }
        else
        {
            Array.Clear(self.WarpVisits, 0, size);
        }
    }

    /// <summary>Internal: <c>Warp.ShuffleSync</c> on the managed stack model. Do not use directly.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="var">The value of the lane.</param>
    /// <param name="srcLane">The source lane.</param>
    /// <param name="width">The logical warp width.</param>
    /// <returns>The value of the source lane.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_stack_warp_shuffle_sync<T>(int frame, int site, string location, uint mask, T var,
        int srcLane, int width = 32) where T : unmanaged
    {
        var at = MeetStackWarp(frame, site, mask, var, "Warp.ShuffleSync", location);
        return __gsharp_warp_shuffle_read_sync(0, ref at, site, mask, var, srcLane, width, location);
    }

    /// <summary>Internal: <c>Warp.ShuffleUpSync</c> on the managed stack model. Do not use directly.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="var">The value of the lane.</param>
    /// <param name="delta">The lane offset.</param>
    /// <param name="width">The logical warp width.</param>
    /// <returns>The value of the lane delta below the caller, or the caller's own value.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_stack_warp_shuffle_up_sync<T>(int frame, int site, string location, uint mask, T var,
        uint delta, int width = 32) where T : unmanaged
    {
        var at = MeetStackWarp(frame, site, mask, var, "Warp.ShuffleUpSync", location);
        return __gsharp_warp_shuffle_read_up_sync(0, ref at, site, mask, var, delta, width, location);
    }

    /// <summary>Internal: <c>Warp.ShuffleDownSync</c> on the managed stack model. Do not use directly.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="var">The value of the lane.</param>
    /// <param name="delta">The lane offset.</param>
    /// <param name="width">The logical warp width.</param>
    /// <returns>The value of the lane delta above the caller, or the caller's own value.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_stack_warp_shuffle_down_sync<T>(int frame, int site, string location, uint mask, T var,
        uint delta, int width = 32) where T : unmanaged
    {
        var at = MeetStackWarp(frame, site, mask, var, "Warp.ShuffleDownSync", location);
        return __gsharp_warp_shuffle_read_down_sync(0, ref at, site, mask, var, delta, width, location);
    }

    /// <summary>Internal: <c>Warp.ShuffleXorSync</c> on the managed stack model. Do not use directly.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="var">The value of the lane.</param>
    /// <param name="laneMask">The lane XOR mask.</param>
    /// <param name="width">The logical warp width.</param>
    /// <returns>The value of the lane laneMask XOR the caller, or the caller's own value.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_stack_warp_shuffle_xor_sync<T>(int frame, int site, string location, uint mask, T var,
        int laneMask, int width = 32) where T : unmanaged
    {
        var at = MeetStackWarp(frame, site, mask, var, "Warp.ShuffleXorSync", location);
        return __gsharp_warp_shuffle_read_xor_sync(0, ref at, site, mask, var, laneMask, width, location);
    }

    /// <summary>Internal: <c>Warp.BallotSync</c> on the managed stack model. Do not use directly.</summary>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="predicate">The predicate of the lane.</param>
    /// <returns>The lanes of the mask whose predicate is true.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static uint __gsharp_stack_warp_ballot_sync(int frame, int site, string location, uint mask,
        bool predicate)
    {
        var at = MeetStackWarp(frame, site, mask, predicate, "Warp.BallotSync", location);
        return __gsharp_warp_ballot_read_sync(0, ref at, site, mask, predicate, location);
    }

    /// <summary>Internal: <c>Warp.AllSync</c> on the managed stack model. Do not use directly.</summary>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="predicate">The predicate of the lane.</param>
    /// <returns>Whether the predicate is true in every lane of the mask.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool __gsharp_stack_warp_all_sync(int frame, int site, string location, uint mask, bool predicate)
    {
        var at = MeetStackWarp(frame, site, mask, predicate, "Warp.AllSync", location);
        return __gsharp_warp_all_read_sync(0, ref at, site, mask, predicate, location);
    }

    /// <summary>Internal: <c>Warp.AnySync</c> on the managed stack model. Do not use directly.</summary>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="predicate">The predicate of the lane.</param>
    /// <returns>Whether the predicate is true in a lane of the mask.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool __gsharp_stack_warp_any_sync(int frame, int site, string location, uint mask, bool predicate)
    {
        var at = MeetStackWarp(frame, site, mask, predicate, "Warp.AnySync", location);
        return __gsharp_warp_any_read_sync(0, ref at, site, mask, predicate, location);
    }

    /// <summary>Internal: <c>Warp.ReduceAddSync</c> on the managed stack model. Do not use directly.</summary>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="val">The value of the lane.</param>
    /// <returns>The sum of the values of the lanes of the mask.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_stack_warp_reduce_add_sync<T>(int frame, int site, string location, uint mask, T val)
        where T : unmanaged, IBinaryInteger<T>
    {
        var at = MeetStackWarp(frame, site, mask, val, "Warp.ReduceAddSync", location);
        return __gsharp_warp_reduce_add_read_sync(0, ref at, site, mask, val, location);
    }

    /// <summary>Internal: <c>Warp.ReduceMinSync</c> on the managed stack model. Do not use directly.</summary>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="val">The value of the lane.</param>
    /// <returns>The smallest value of the lanes of the mask.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_stack_warp_reduce_min_sync<T>(int frame, int site, string location, uint mask, T val)
        where T : unmanaged, IBinaryInteger<T>
    {
        var at = MeetStackWarp(frame, site, mask, val, "Warp.ReduceMinSync", location);
        return __gsharp_warp_reduce_min_read_sync(0, ref at, site, mask, val, location);
    }

    /// <summary>Internal: <c>Warp.ReduceMaxSync</c> on the managed stack model. Do not use directly.</summary>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="val">The value of the lane.</param>
    /// <returns>The largest value of the lanes of the mask.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_stack_warp_reduce_max_sync<T>(int frame, int site, string location, uint mask, T val)
        where T : unmanaged, IBinaryInteger<T>
    {
        var at = MeetStackWarp(frame, site, mask, val, "Warp.ReduceMaxSync", location);
        return __gsharp_warp_reduce_max_read_sync(0, ref at, site, mask, val, location);
    }

    /// <summary>Internal: <c>Warp.ReduceAndSync</c> on the managed stack model. Do not use directly.</summary>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="val">The value of the lane.</param>
    /// <returns>The bitwise AND of the values of the lanes of the mask.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_stack_warp_reduce_and_sync<T>(int frame, int site, string location, uint mask, T val)
        where T : unmanaged, IBinaryInteger<T>
    {
        var at = MeetStackWarp(frame, site, mask, val, "Warp.ReduceAndSync", location);
        return __gsharp_warp_reduce_and_read_sync(0, ref at, site, mask, val, location);
    }

    /// <summary>Internal: <c>Warp.ReduceOrSync</c> on the managed stack model. Do not use directly.</summary>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="val">The value of the lane.</param>
    /// <returns>The bitwise OR of the values of the lanes of the mask.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_stack_warp_reduce_or_sync<T>(int frame, int site, string location, uint mask, T val)
        where T : unmanaged, IBinaryInteger<T>
    {
        var at = MeetStackWarp(frame, site, mask, val, "Warp.ReduceOrSync", location);
        return __gsharp_warp_reduce_or_read_sync(0, ref at, site, mask, val, location);
    }

    /// <summary>Internal: <c>Warp.ReduceXorSync</c> on the managed stack model. Do not use directly.</summary>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="val">The value of the lane.</param>
    /// <returns>The bitwise XOR of the values of the lanes of the mask.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_stack_warp_reduce_xor_sync<T>(int frame, int site, string location, uint mask, T val)
        where T : unmanaged, IBinaryInteger<T>
    {
        var at = MeetStackWarp(frame, site, mask, val, "Warp.ReduceXorSync", location);
        return __gsharp_warp_reduce_xor_read_sync(0, ref at, site, mask, val, location);
    }

    /// <summary>Internal: <c>Gpu.MatchAnySync</c> on the managed stack model. Do not use directly.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="value">The value to compare.</param>
    /// <returns>The lanes of the mask whose value equals the caller's.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static uint __gsharp_stack_warp_match_any_sync<T>(int frame, int site, string location, uint mask, T value)
        where T : unmanaged
    {
        var at = MeetStackWarp(frame, site, mask, value, "Gpu.MatchAnySync", location);
        return __gsharp_warp_match_any_read_sync(0, ref at, site, mask, value, location);
    }

    /// <summary>Internal: <c>Gpu.MatchAllSync</c> on the managed stack model. Do not use directly.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    /// <param name="value">The value to compare.</param>
    /// <param name="pred">Set to 1 when every lane that takes part holds the same value, or 0 otherwise.</param>
    /// <returns>The lanes that take part when all of them match, or 0 otherwise.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static uint __gsharp_stack_warp_match_all_sync<T>(int frame, int site, string location, uint mask, T value,
        ref int pred) where T : unmanaged
    {
        var at = MeetStackWarp(frame, site, mask, value, "Gpu.MatchAllSync", location);
        return __gsharp_warp_match_all_read_sync(0, ref at, site, mask, value, ref pred, location);
    }

    /// <summary>Internal: <c>Gpu.SyncWarp</c> on the managed stack model. Do not use directly.</summary>
    /// <param name="frame">The offset of the caller's frame in the visit counts.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="location">The place of the call in the C# source, for the error messages.</param>
    /// <param name="mask">The lanes that take part.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void __gsharp_stack_warp_syncwarp(int frame, int site, string location, uint mask = 0xFFFFFFFFu)
    {
        var at = MeetStackWarp(frame, site, mask, 0, "Gpu.SyncWarp", location);
        __gsharp_warp_sync_read(0, ref at, site, mask, location);
    }

    // [WHY] Publishes, then always gives the baton to the scheduler, also when no lane of the mask is missing: the lanes
    // of a group must go on in the same round (WarpCanRun), and a lane publishes at most once per round, which the two
    // record slots of a lane rely on. The scheduler runs the lane again once the lanes of its mask met there
    private static ManagedStackWarpFrame MeetStackWarp<T>(int frame, int site, uint mask, T value, string name,
        string location) where T : unmanaged
    {
        var self = StackWarpSelf();
        var at = new ManagedStackWarpFrame(self.WarpVisits, frame);
        __gsharp_warp_publish(0, ref at, site, mask, value, name, location);
        var launch = self.Launch ?? throw new InvalidOperationException(
            "G#: internal error: a warp collective of the stack model runs outside a launch " +
            "(please report this G# bug)");
        self.AtWarp = true;
        ManagedBaton.HandOver(launch.Scheduler, self.Go);
        self.AtWarp = false;
        if (launch.Cancelled)
        {
            throw new ManagedLaunchCancelledException();
        }

        return at;
    }

    // The GPU thread that the calling OS thread runs on the managed stack model
    private static ManagedStackThread StackWarpSelf() =>
        t_stackThread ?? throw new InvalidOperationException(
            "G#: internal error: a warp collective of the stack model runs outside a managed stack launch " +
            "(please report this G# bug)");

    // A frame of the managed stack model: its offset in the visit counts of the GPU thread is its path, the same in
    // every lane, and the count of its collective site s is at offset + 1 + s
    private readonly struct ManagedStackWarpFrame(uint[] visits, int frame) : IManagedWarpFrame
    {
        public uint WarpPath => (uint)frame;

        public uint AdvanceWarpVisit(int site) => ++visits[frame + 1 + site];

        public uint GetWarpVisit(int site) => visits[frame + 1 + site];
    }
}
