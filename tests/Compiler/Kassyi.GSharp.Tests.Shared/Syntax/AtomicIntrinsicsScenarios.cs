using System;
using System.Collections.Generic;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="AtomicIntrinsicsKernels"/> through a runner, so that the native dialects
/// (<see cref="AtomicIntrinsicsTests"/>) and the managed path (<c>AtomicIntrinsicsManagedTests</c>, #728) check the same
/// properties.
/// </summary>
/// <remarks>
/// The GPU runs the atomics of one address one at a time in an order it does not define. Every path must give a final
/// value and old values that some order of the operations explains: the checks rebuild that order from the old values
/// (<see cref="AssertOneOrder{T}"/>) instead of assuming the order in which the CPU paths step the threads.
/// </remarks>
internal static class AtomicIntrinsicsScenarios
{
    private const int WarpSizedBlock = 32;

    public static void Arithmetic(Action<Action> run)
    {
        int[] intState = [0, int.MaxValue];
        uint[] uintState = [uint.MaxValue, 0u, uint.MaxValue, 0u];
        ulong[] ulongState = [0ul, ulong.MaxValue, ulong.MaxValue];
        float[] floatState = [0.0f, 0.0f];

        run(() => AtomicIntrinsicsKernels.LaunchAtomicArithmeticKernel(
            intState, uintState, ulongState, floatState, blockDimX: WarpSizedBlock));

        Assert.Equal(-WarpSizedBlock, intState[0]);
        Assert.Equal(-5, intState[1]);

        Assert.Equal(0u, uintState[0]);
        Assert.Equal(uint.MaxValue, uintState[1]);
        Assert.Equal(0u, uintState[2]);
        Assert.Equal(uint.MaxValue, uintState[3]);

        Assert.Equal(0xFFFFFFFF_00000000ul, ulongState[0]);
        Assert.Equal(0ul, ulongState[1]);
        Assert.Equal(0xFFFFFFFF_00000000ul, ulongState[2]);

        Assert.Equal(WarpSizedBlock * 0.5f, floatState[0]);
        Assert.Equal(3.25f, floatState[1]);
    }

    public static void IncDecWrap(Action<Action> run, uint initial, uint limit)
    {
        uint[] state = [initial, initial];

        run(() => AtomicIntrinsicsKernels.LaunchAtomicIncDecWrapKernel(state, limit, blockDimX: WarpSizedBlock));

        // Every thread applies the same operation, so the result is independent of execution order.
        uint expectedInc = initial;
        uint expectedDec = initial;
        for (int i = 0; i < WarpSizedBlock; i++)
        {
            expectedInc = Inc(expectedInc, limit);
            expectedDec = Dec(expectedDec, limit);
        }

        Assert.Equal(expectedInc, state[0]);
        Assert.Equal(expectedDec, state[1]);
    }

    public static void FloatAddHighContention(Action<Action> run)
    {
        float[] globalSum = [0.0f];
        const uint threadsPerBlock = 256;
        const uint blocksPerGrid = 16;

        run(() => AtomicIntrinsicsKernels.LaunchAtomicFloatAddStressKernel(
            globalSum, gridDimX: blocksPerGrid, blockDimX: threadsPerBlock));

        // 4096 is exactly representable as float, so summation order introduces no rounding error.
        Assert.Equal(threadsPerBlock * blocksPerGrid, globalSum[0]);
    }

    public static void ThroughRefParameter(Action<Action> run)
    {
        int[] counter = [0];

        run(() => AtomicIntrinsicsKernels.LaunchAtomicRefParameterKernel(counter, blockDimX: WarpSizedBlock));

        Assert.Equal(WarpSizedBlock * (WarpSizedBlock + 1) / 2, counter[0]);
    }

    // Each slot starts where its operation wraps or saturates within the 32 operations (Add / Sub across the end of the
    // range, Inc / Dec across the limit 9), so a path that computes in a wider type or skips the wrap gives other values.
    // The unsigned Min / Max values cross the top bit (t << 27, t << 59), so a path that compares them as signed
    // integers gives other values too
    public static void OldValues(Action<Action> run)
    {
        const int n = WarpSizedBlock;
        int[] ints = [int.MaxValue - 15, int.MinValue + 15, 0, 0, int.MaxValue, -1, 0, 0];
        uint[] uints = [uint.MaxValue - 15u, 15u, 0u, 0u, uint.MaxValue, 0u, uint.MaxValue, 0u, 0u, 0u, 0u, 0u];
        ulong[] ulongs = [ulong.MaxValue - 15ul, 0ul, 0ul, ulong.MaxValue, 0ul, ulong.MaxValue, 0ul, 0ul, 0ul];
        float[] floats = [0f, 0f, 0f];
        int[] initialInts = (int[])ints.Clone();
        uint[] initialUints = (uint[])uints.Clone();
        ulong[] initialUlongs = (ulong[])ulongs.Clone();
        float[] initialFloats = (float[])floats.Clone();
        var intOld = new int[ints.Length * n];
        var uintOld = new uint[uints.Length * n];
        var ulongOld = new ulong[ulongs.Length * n];
        var floatOld = new float[floats.Length * n];

        run(() => AtomicIntrinsicsKernels.LaunchAtomicOldValueKernel(
            ints, intOld, uints, uintOld, ulongs, ulongOld, floats, floatOld, blockDimX: n));

        AssertOneOrder("int Add", initialInts[0], ints[0], intOld, 0, static (s, _) => unchecked(s + 1));
        AssertOneOrder("int Sub", initialInts[1], ints[1], intOld, 1, static (s, _) => unchecked(s - 1));
        AssertOneOrder("int Exch", initialInts[2], ints[2], intOld, 2, static (_, t) => t + 1);
        AssertOneOrder("int CAS", initialInts[3], ints[3], intOld, 3, static (s, t) => s == 0 ? t + 1 : s);
        AssertOneOrder("int Min", initialInts[4], ints[4], intOld, 4, static (s, t) => Math.Min(s, 16 - t));
        AssertOneOrder("int And", initialInts[5], ints[5], intOld, 5, static (s, t) => s & ~(1 << t));
        AssertOneOrder("int Xor", initialInts[6], ints[6], intOld, 6, static (s, t) => s ^ (1 << t));
        Assert.Equal(n * (n + 1) / 2, ints[7]);

        AssertOneOrder("uint Add", initialUints[0], uints[0], uintOld, 0, static (s, _) => unchecked(s + 1u));
        AssertOneOrder("uint Sub", initialUints[1], uints[1], uintOld, 1, static (s, _) => unchecked(s - 1u));
        AssertOneOrder("uint Exch", initialUints[2], uints[2], uintOld, 2, static (_, t) => (uint)t + 1u);
        AssertOneOrder("uint CAS", initialUints[3], uints[3], uintOld, 3, static (s, t) => s == 0u ? (uint)t + 1u : s);
        AssertOneOrder("uint Min", initialUints[4], uints[4], uintOld, 4,
            static (s, t) => Math.Min(s, (uint)(31 - t) << 27));
        AssertOneOrder("uint Max", initialUints[5], uints[5], uintOld, 5, static (s, t) => Math.Max(s, (uint)t << 27));
        AssertOneOrder("uint And", initialUints[6], uints[6], uintOld, 6, static (s, t) => s & ~(1u << t));
        AssertOneOrder("uint Or", initialUints[7], uints[7], uintOld, 7, static (s, t) => s | (1u << t));
        AssertOneOrder("uint Xor", initialUints[8], uints[8], uintOld, 8, static (s, t) => s ^ (1u << t));
        AssertOneOrder("uint Inc", initialUints[9], uints[9], uintOld, 9, static (s, _) => Inc(s, 9u));
        AssertOneOrder("uint Dec", initialUints[10], uints[10], uintOld, 10, static (s, _) => Dec(s, 9u));
        Assert.Equal((uint)(n * (n + 1) / 2), uints[11]);

        AssertOneOrder("ulong Add", initialUlongs[0], ulongs[0], ulongOld, 0, static (s, _) => unchecked(s + 1ul));
        AssertOneOrder("ulong Exch", initialUlongs[1], ulongs[1], ulongOld, 1, static (_, t) => (ulong)t + 1ul);
        AssertOneOrder("ulong CAS", initialUlongs[2], ulongs[2], ulongOld, 2,
            static (s, t) => s == 0ul ? (ulong)t + 1ul : s);
        AssertOneOrder("ulong Min", initialUlongs[3], ulongs[3], ulongOld, 3,
            static (s, t) => Math.Min(s, (ulong)(31 - t) << 59));
        AssertOneOrder("ulong Max", initialUlongs[4], ulongs[4], ulongOld, 4,
            static (s, t) => Math.Max(s, (ulong)t << 59));
        AssertOneOrder("ulong And", initialUlongs[5], ulongs[5], ulongOld, 5,
            static (s, t) => s & ~(1ul << (t + 32)));
        AssertOneOrder("ulong Or", initialUlongs[6], ulongs[6], ulongOld, 6, static (s, t) => s | (1ul << (t + 32)));
        AssertOneOrder("ulong Xor", initialUlongs[7], ulongs[7], ulongOld, 7, static (s, t) => s ^ (1ul << (t + 32)));
        Assert.Equal((ulong)(n * (n + 1) / 2), ulongs[8]);

        // Every partial sum of ones is exact in float, so any order gives the same values
        AssertOneOrder("float Add", initialFloats[0], floats[0], floatOld, 0, static (s, _) => s + 1f);
        AssertOneOrder("float Exch", initialFloats[1], floats[1], floatOld, 1, static (_, t) => (float)(t + 1));
        Assert.Equal(n * 0.5f, floats[2]);
    }

    // CUDA atomicInc / atomicDec: inc wraps to 0 once old >= limit; dec wraps to limit when old == 0 or old > limit
    private static uint Inc(uint old, uint limit) => old >= limit ? 0u : old + 1u;

    private static uint Dec(uint old, uint limit) => old == 0u || old > limit ? limit : old - 1u;

    /// <summary>
    /// Asserts that one order of the operations of slot <paramref name="slot"/> gives each thread the old value it
    /// recorded and leaves <paramref name="final"/>: thread t applies <c>apply(state, t)</c> to the state it saw.
    /// </summary>
    /// <remarks>
    /// The order is rebuilt one step at a time. Among the threads that recorded the current state, those whose operation
    /// leaves the state as it is can run first in any order. At most one of the others can be next, because once it
    /// changes the state no other thread can have seen it, unless the state comes back (Inc / Dec, which every thread
    /// applies alike, so any of them will do). A path that returns the new value instead of the old one, or that loses
    /// an update, leaves no thread for some state and fails.
    /// <para>
    /// The premise: a state comes back to an earlier value only in a slot where every thread applies the same operation
    /// (Add / Sub of one unit, Inc / Dec). A new slot must keep that, by giving each thread its own operand (t + 1,
    /// bit t) where the state could come back, or the check may reject a correct order.
    /// </para>
    /// </remarks>
    private static void AssertOneOrder<T>(string name, T initial, T final, T[] olds, int slot, Func<T, int, T> apply)
        where T : IEquatable<T>
    {
        const int n = WarpSizedBlock;
        var done = new bool[n];
        var state = initial;
        for (var remaining = n; remaining > 0;)
        {
            var progressed = false;
            for (var t = 0; t < n; t++)
            {
                if (!done[t] && olds[slot * n + t].Equals(state) && apply(state, t).Equals(state))
                {
                    done[t] = true;
                    remaining--;
                    progressed = true;
                }
            }

            int next = -1;
            for (var t = 0; t < n && next < 0; t++)
            {
                if (!done[t] && olds[slot * n + t].Equals(state))
                {
                    next = t;
                }
            }

            if (next >= 0)
            {
                state = apply(state, next);
                done[next] = true;
                remaining--;
            }
            else if (!progressed && remaining > 0)
            {
                Assert.Fail($"{name}: no thread saw the state {state}; old values: {Describe(olds, slot)}");
            }
        }

        Assert.True(final.Equals(state), $"{name}: final value {final}, expected {state} after the rebuilt order");
    }

    private static string Describe<T>(T[] olds, int slot)
    {
        var values = new List<string>(WarpSizedBlock);
        for (var t = 0; t < WarpSizedBlock; t++)
        {
            values.Add($"{t}:{olds[slot * WarpSizedBlock + t]}");
        }

        return string.Join(", ", values);
    }
}
