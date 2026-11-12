using System.ComponentModel;
using System.Numerics;

namespace Kassyi.GSharp.Core;

// The managed forms of Gpu.LaneId(), Gpu.WarpId(), Gpu.SmId() and the warp votes and reductions (#845).
// For a vote, each lane publishes its predicate or value through __gsharp_warp_publish before a state boundary;
// after it, every lane reads the values that the lanes of the mask published for the same instance of the call
// (#1468). The native CPU prelude (WarpAndHints.hpp) computes the same.
public static partial class ManagedGpuContext
{
    /// <summary>
    /// The managed form of <c>Gpu.LaneId()</c>, to which its stub forwards: the linear thread index in the block (x
    /// fastest) modulo 32, as on the GPU (#845). Outside a managed launch it throws <see cref="GpuOnlyException"/>.
    /// </summary>
    /// <returns>The lane of the calling thread in its warp, 0 to 31.</returns>
    internal static int LaneId() => IsActive ? LinearThreadId & 31 : GpuOnlyException.Throw<int>();

    /// <summary>
    /// The managed form of <c>Gpu.WarpId()</c>, to which its stub forwards: the linear thread index in the block (x
    /// fastest) divided by 32, as on the GPU (#845). Outside a managed launch it throws <see cref="GpuOnlyException"/>.
    /// </summary>
    /// <returns>The warp of the calling thread in its block.</returns>
    internal static int WarpId() => IsActive ? LinearThreadId >> 5 : GpuOnlyException.Throw<int>();

    /// <summary>
    /// The managed form of <c>Gpu.SmId()</c>, to which its stub forwards: 0, as the native CPU prelude gives
    /// (WarpAndHints.hpp). The CPU runs a launch as one streaming multiprocessor, and 0 is a number a GPU also gives.
    /// Outside a managed launch it throws <see cref="GpuOnlyException"/>.
    /// </summary>
    /// <returns>0.</returns>
    internal static int SmId() => IsActive ? 0 : GpuOnlyException.Throw<int>();

    // The lanes of lanes whose published predicate is true
    private static uint BallotLanes(int linearTid, in WarpInstance instance, uint mask, uint lanes, string name,
        string location)
    {
        var result = 0u;
        for (var srcLane = 0; srcLane < 32; srcLane++)
        {
            if ((lanes & (1u << srcLane)) != 0u &&
                ReadShuffleLane<bool>(linearTid, instance, mask, srcLane, name, location))
            {
                result |= 1u << srcLane;
            }
        }

        return result;
    }

    // The published values of the lanes that take part, combined in lane order
    private static T ReduceLanes<TFrame, T>(ref TFrame frame, int site, uint mask, T val, string name, string location,
        Func<T, T, T> combine) where TFrame : IManagedWarpFrame where T : unmanaged
    {
        var instance = BeginWarpRead(ref frame, site);
        var linearTid = LinearThreadId;
        var lanes = GetVoteLanes(linearTid, instance, mask, name, location);
        var result = val;
        var first = true;
        for (var srcLane = 0; srcLane < 32; srcLane++)
        {
            if ((lanes & (1u << srcLane)) == 0u)
            {
                continue;
            }

            var laneVal = ReadShuffleLane<T>(linearTid, instance, mask, srcLane, name, location);
            result = first ? laneVal : combine(result, laneVal);
            first = false;
        }

        return result;
    }

    /// <summary>Internal mock for <c>Warp.BallotSync</c>. Do not use directly.</summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <param name="root">Unused: the managed path keeps the published values itself.</param>
    /// <param name="frame">The frame the call is in.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="pred">The predicate of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The lanes of the mask whose predicate is true.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static uint __gsharp_warp_ballot_read_sync<TFrame>(int root, ref TFrame frame, int site, uint mask,
        bool pred, string location) where TFrame : IManagedWarpFrame
    {
        const string Name = "Warp.BallotSync";
        var instance = BeginWarpRead(ref frame, site);
        var linearTid = LinearThreadId;
        var lanes = GetVoteLanes(linearTid, instance, mask, Name, location);
        return BallotLanes(linearTid, instance, mask, lanes, Name, location);
    }

    /// <summary>Internal mock for <c>Warp.AllSync</c>. Do not use directly.</summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <param name="root">Unused: the managed path keeps the published values itself.</param>
    /// <param name="frame">The frame the call is in.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="pred">The predicate of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>Whether the predicate is true in every lane of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool __gsharp_warp_all_read_sync<TFrame>(int root, ref TFrame frame, int site, uint mask,
        bool pred, string location) where TFrame : IManagedWarpFrame
    {
        const string Name = "Warp.AllSync";
        var instance = BeginWarpRead(ref frame, site);
        var linearTid = LinearThreadId;
        var lanes = GetVoteLanes(linearTid, instance, mask, Name, location);
        return BallotLanes(linearTid, instance, mask, lanes, Name, location) == lanes;
    }

    /// <summary>Internal mock for <c>Warp.AnySync</c>. Do not use directly.</summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <param name="root">Unused: the managed path keeps the published values itself.</param>
    /// <param name="frame">The frame the call is in.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="pred">The predicate of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>Whether the predicate is true in a lane of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool __gsharp_warp_any_read_sync<TFrame>(int root, ref TFrame frame, int site, uint mask,
        bool pred, string location) where TFrame : IManagedWarpFrame
    {
        const string Name = "Warp.AnySync";
        var instance = BeginWarpRead(ref frame, site);
        var linearTid = LinearThreadId;
        var lanes = GetVoteLanes(linearTid, instance, mask, Name, location);
        return BallotLanes(linearTid, instance, mask, lanes, Name, location) != 0u;
    }

    /// <summary>
    /// Internal mock for <c>Warp.ReduceAddSync</c>: the sum, wrapping around like the GPU. Do not use directly.
    /// </summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="root">Unused: the managed path keeps the published values itself.</param>
    /// <param name="frame">The frame the call is in.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="val">The value of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The sum of the values of the lanes of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_reduce_add_read_sync<TFrame, T>(
        int root, ref TFrame frame, int site, uint mask, T val,
        string location) where TFrame : IManagedWarpFrame where T : unmanaged, IBinaryInteger<T> =>
        ReduceLanes(ref frame, site, mask, val, "Warp.ReduceAddSync", location, static (a, b) => unchecked(a + b));

    /// <summary>Internal mock for <c>Warp.ReduceMinSync</c>. Do not use directly.</summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>, compared with its own sign.</typeparam>
    /// <param name="root">Unused: the managed path keeps the published values itself.</param>
    /// <param name="frame">The frame the call is in.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="val">The value of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The smallest value of the lanes of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_reduce_min_read_sync<TFrame, T>(
        int root, ref TFrame frame, int site, uint mask, T val,
        string location) where TFrame : IManagedWarpFrame where T : unmanaged, IBinaryInteger<T> =>
        ReduceLanes(ref frame, site, mask, val, "Warp.ReduceMinSync", location, T.Min);

    /// <summary>Internal mock for <c>Warp.ReduceMaxSync</c>. Do not use directly.</summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>, compared with its own sign.</typeparam>
    /// <param name="root">Unused: the managed path keeps the published values itself.</param>
    /// <param name="frame">The frame the call is in.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="val">The value of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The largest value of the lanes of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_reduce_max_read_sync<TFrame, T>(
        int root, ref TFrame frame, int site, uint mask, T val,
        string location) where TFrame : IManagedWarpFrame where T : unmanaged, IBinaryInteger<T> =>
        ReduceLanes(ref frame, site, mask, val, "Warp.ReduceMaxSync", location, T.Max);

    /// <summary>Internal mock for <c>Warp.ReduceAndSync</c>. Do not use directly.</summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="root">Unused: the managed path keeps the published values itself.</param>
    /// <param name="frame">The frame the call is in.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="val">The value of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The bitwise AND of the values of the lanes of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_reduce_and_read_sync<TFrame, T>(
        int root, ref TFrame frame, int site, uint mask, T val,
        string location) where TFrame : IManagedWarpFrame where T : unmanaged, IBinaryInteger<T> =>
        ReduceLanes(ref frame, site, mask, val, "Warp.ReduceAndSync", location, static (a, b) => a & b);

    /// <summary>Internal mock for <c>Warp.ReduceOrSync</c>. Do not use directly.</summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="root">Unused: the managed path keeps the published values itself.</param>
    /// <param name="frame">The frame the call is in.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="val">The value of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The bitwise OR of the values of the lanes of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_reduce_or_read_sync<TFrame, T>(int root, ref TFrame frame, int site, uint mask, T val,
        string location) where TFrame : IManagedWarpFrame where T : unmanaged, IBinaryInteger<T> =>
        ReduceLanes(ref frame, site, mask, val, "Warp.ReduceOrSync", location, static (a, b) => a | b);

    /// <summary>Internal mock for <c>Warp.ReduceXorSync</c>. Do not use directly.</summary>
    /// <typeparam name="TFrame">The frame of the state machine.</typeparam>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="root">Unused: the managed path keeps the published values itself.</param>
    /// <param name="frame">The frame the call is in.</param>
    /// <param name="site">The number of the call in the body of its function.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="val">The value of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The bitwise XOR of the values of the lanes of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_reduce_xor_read_sync<TFrame, T>(
        int root, ref TFrame frame, int site, uint mask, T val,
        string location) where TFrame : IManagedWarpFrame where T : unmanaged, IBinaryInteger<T> =>
        ReduceLanes(ref frame, site, mask, val, "Warp.ReduceXorSync", location, static (a, b) => a ^ b);
}
