using System.ComponentModel;
using System.Numerics;

namespace Kassyi.GSharp.Core;

// The managed forms of the warp votes and reductions (#845). Each lane publishes its predicate or value through
// __gsharp_warp_exchange_assign before a state boundary; after it, every lane reads the values that the lanes of the
// mask published for the same exchange. The native CPU prelude (WarpAndHints.hpp) computes the same.
public static partial class ManagedGpuContext
{
    // The lanes that take part: the lanes of mask that exist in the block. The caller must be one of them; CUDA leaves the
    // result undefined otherwise.
    private static uint GetVoteLanes(int linearTid, uint mask, string name, string location)
    {
        var laneId = linearTid & 31;
        if ((mask & (1u << laneId)) == 0u)
        {
            throw new InvalidOperationException(
                $"G#: {name} at {location} has mask 0x{mask:X8} without caller lane {laneId} (CUDA leaves the result undefined)");
        }

        return ActiveMask() & mask;
    }

    // The lanes of lanes whose published predicate is true
    private static uint BallotLanes(int linearTid, uint lanes, string name, string location)
    {
        var result = 0u;
        for (var srcLane = 0; srcLane < 32; srcLane++)
        {
            if ((lanes & (1u << srcLane)) != 0u && ReadShuffleLane<bool>(linearTid, srcLane, name, location))
            {
                result |= 1u << srcLane;
            }
        }

        return result;
    }

    // The published values of lanes, combined in lane order
    private static T ReduceLanes<T>(uint mask, T val, string name, string location, Func<T, T, T> combine)
        where T : unmanaged
    {
        var linearTid = LinearThreadId;
        AdvanceWarpShuffleCount(linearTid);
        var lanes = GetVoteLanes(linearTid, mask, name, location);
        var result = val;
        var first = true;
        for (var srcLane = 0; srcLane < 32; srcLane++)
        {
            if ((lanes & (1u << srcLane)) == 0u)
            {
                continue;
            }

            var laneVal = ReadShuffleLane<T>(linearTid, srcLane, name, location);
            result = first ? laneVal : combine(result, laneVal);
            first = false;
        }

        return result;
    }

    /// <summary>Internal mock for <c>Warp.BallotSync</c>. Do not use directly.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="pred">The predicate of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The lanes of the mask whose predicate is true.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static uint __gsharp_warp_ballot_read_sync<TContext>(TContext ctx, uint mask, bool pred, string location)
    {
        const string Name = "Warp.BallotSync";
        var linearTid = LinearThreadId;
        AdvanceWarpShuffleCount(linearTid);
        return BallotLanes(linearTid, GetVoteLanes(linearTid, mask, Name, location), Name, location);
    }

    /// <summary>Internal mock for <c>Warp.AllSync</c>. Do not use directly.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="pred">The predicate of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>Whether the predicate is true in every lane of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool __gsharp_warp_all_read_sync<TContext>(TContext ctx, uint mask, bool pred, string location)
    {
        const string Name = "Warp.AllSync";
        var linearTid = LinearThreadId;
        AdvanceWarpShuffleCount(linearTid);
        var lanes = GetVoteLanes(linearTid, mask, Name, location);
        return BallotLanes(linearTid, lanes, Name, location) == lanes;
    }

    /// <summary>Internal mock for <c>Warp.AnySync</c>. Do not use directly.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="pred">The predicate of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>Whether the predicate is true in a lane of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool __gsharp_warp_any_read_sync<TContext>(TContext ctx, uint mask, bool pred, string location)
    {
        const string Name = "Warp.AnySync";
        var linearTid = LinearThreadId;
        AdvanceWarpShuffleCount(linearTid);
        return BallotLanes(linearTid, GetVoteLanes(linearTid, mask, Name, location), Name, location) != 0u;
    }

    /// <summary>Internal mock for <c>Warp.ReduceAddSync</c>: the sum, wrapping around like the GPU. Do not use directly.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="val">The value of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The sum of the values of the lanes of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_reduce_add_read_sync<TContext, T>(TContext ctx, uint mask, T val, string location)
        where T : unmanaged, IBinaryInteger<T> =>
        ReduceLanes(mask, val, "Warp.ReduceAddSync", location, static (a, b) => unchecked(a + b));

    /// <summary>Internal mock for <c>Warp.ReduceMinSync</c>. Do not use directly.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>, compared with its own sign.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="val">The value of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The smallest value of the lanes of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_reduce_min_read_sync<TContext, T>(TContext ctx, uint mask, T val, string location)
        where T : unmanaged, IBinaryInteger<T> =>
        ReduceLanes(mask, val, "Warp.ReduceMinSync", location, T.Min);

    /// <summary>Internal mock for <c>Warp.ReduceMaxSync</c>. Do not use directly.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>, compared with its own sign.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="val">The value of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The largest value of the lanes of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_reduce_max_read_sync<TContext, T>(TContext ctx, uint mask, T val, string location)
        where T : unmanaged, IBinaryInteger<T> =>
        ReduceLanes(mask, val, "Warp.ReduceMaxSync", location, T.Max);

    /// <summary>Internal mock for <c>Warp.ReduceAndSync</c>. Do not use directly.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="val">The value of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The bitwise AND of the values of the lanes of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_reduce_and_read_sync<TContext, T>(TContext ctx, uint mask, T val, string location)
        where T : unmanaged, IBinaryInteger<T> =>
        ReduceLanes(mask, val, "Warp.ReduceAndSync", location, static (a, b) => a & b);

    /// <summary>Internal mock for <c>Warp.ReduceOrSync</c>. Do not use directly.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="val">The value of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The bitwise OR of the values of the lanes of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_reduce_or_read_sync<TContext, T>(TContext ctx, uint mask, T val, string location)
        where T : unmanaged, IBinaryInteger<T> =>
        ReduceLanes(mask, val, "Warp.ReduceOrSync", location, static (a, b) => a | b);

    /// <summary>Internal mock for <c>Warp.ReduceXorSync</c>. Do not use directly.</summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <typeparam name="T"><see cref="int"/> or <see cref="uint"/>.</typeparam>
    /// <param name="ctx">The execution context.</param>
    /// <param name="mask">The participating lane mask.</param>
    /// <param name="val">The value of the caller.</param>
    /// <param name="location">The place of the call in the C# source, for the error message.</param>
    /// <returns>The bitwise XOR of the values of the lanes of the mask.</returns>
    /// <exception cref="InvalidOperationException">The mask does not contain the caller's lane.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static T __gsharp_warp_reduce_xor_read_sync<TContext, T>(TContext ctx, uint mask, T val, string location)
        where T : unmanaged, IBinaryInteger<T> =>
        ReduceLanes(mask, val, "Warp.ReduceXorSync", location, static (a, b) => a ^ b);
}
