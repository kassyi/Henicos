using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Launches and checks the kernels of <see cref="AtomicIntrinsicsKernels"/> through a runner, so that the native dialects
/// (<see cref="AtomicIntrinsicsTests"/>) and the managed path (<c>AtomicIntrinsicsManagedTests</c>, #728) check the same
/// properties.
/// </summary>
/// <remarks>
/// The GPU runs the atomics of one address one at a time in an order it does not define. The checks of the old values
/// hold for any order of the operations (<see cref="AssertSameStep{T}"/> and the others) instead of assuming the order in
/// which the CPU paths step the threads, and fail for an operation that returns the new value, loses an update, skips a
/// wrap or compares unsigned values as signed.
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
    // integers gives other values too. Slots left out start at 0
    public static void OldValues(Action<Action> run)
    {
        var ints = new Slots<int>(IntSlot.Count)
        {
            [IntSlot.Add] = int.MaxValue - 15,
            [IntSlot.Sub] = int.MinValue + 15,
            [IntSlot.Min] = int.MaxValue,
            [IntSlot.And] = -1,
        };
        var uints = new Slots<uint>(UintSlot.Count)
        {
            [UintSlot.Add] = uint.MaxValue - 15u,
            [UintSlot.Sub] = 15u,
            [UintSlot.Min] = uint.MaxValue,
            [UintSlot.And] = uint.MaxValue,
        };
        var ulongs = new Slots<ulong>(UlongSlot.Count)
        {
            [UlongSlot.Add] = ulong.MaxValue - 15ul,
            [UlongSlot.Min] = ulong.MaxValue,
            [UlongSlot.And] = ulong.MaxValue,
        };
        var floats = new Slots<float>(FloatSlot.Count);

        run(() => AtomicIntrinsicsKernels.LaunchAtomicOldValueKernel(ints.Values, ints.Olds, uints.Values, uints.Olds,
            ulongs.Values, ulongs.Olds, floats.Values, floats.Olds, blockDimX: WarpSizedBlock));

        AssertSameStep(ints, IntSlot.Add, static s => unchecked(s + 1));
        AssertSameStep(ints, IntSlot.Sub, static s => unchecked(s - 1));
        AssertExchange(ints, IntSlot.Exch, static t => t + 1);
        AssertCompareExchange(ints, IntSlot.Cas, static t => t + 1);
        AssertMin(ints, IntSlot.Min, static t => 16 - t);
        AssertBits(ints, IntSlot.And, static t => 1 << t);
        AssertBits(ints, IntSlot.Xor, static t => 1 << t);
        Assert.Equal(WarpSizedBlock * (WarpSizedBlock + 1) / 2, ints.Final(IntSlot.Reduction));

        AssertSameStep(uints, UintSlot.Add, static s => unchecked(s + 1u));
        AssertSameStep(uints, UintSlot.Sub, static s => unchecked(s - 1u));
        AssertExchange(uints, UintSlot.Exch, static t => (uint)t + 1u);
        AssertCompareExchange(uints, UintSlot.Cas, static t => (uint)t + 1u);
        AssertMin(uints, UintSlot.Min, static t => (uint)(31 - t) << 27);
        AssertMax(uints, UintSlot.Max, static t => (uint)t << 27);
        AssertBits(uints, UintSlot.And, static t => 1u << t);
        AssertBits(uints, UintSlot.Or, static t => 1u << t);
        AssertBits(uints, UintSlot.Xor, static t => 1u << t);
        AssertSameStep(uints, UintSlot.Inc, static s => Inc(s, 9u));
        AssertSameStep(uints, UintSlot.Dec, static s => Dec(s, 9u));
        Assert.Equal((uint)(WarpSizedBlock * (WarpSizedBlock + 1) / 2), uints.Final(UintSlot.Reduction));

        AssertSameStep(ulongs, UlongSlot.Add, static s => unchecked(s + 1ul));
        AssertExchange(ulongs, UlongSlot.Exch, static t => (ulong)t + 1ul);
        AssertCompareExchange(ulongs, UlongSlot.Cas, static t => (ulong)t + 1ul);
        AssertMin(ulongs, UlongSlot.Min, static t => (ulong)(31 - t) << 59);
        AssertMax(ulongs, UlongSlot.Max, static t => (ulong)t << 59);
        AssertBits(ulongs, UlongSlot.And, static t => 1ul << (t + 32));
        AssertBits(ulongs, UlongSlot.Or, static t => 1ul << (t + 32));
        AssertBits(ulongs, UlongSlot.Xor, static t => 1ul << (t + 32));
        Assert.Equal((ulong)(WarpSizedBlock * (WarpSizedBlock + 1) / 2), ulongs.Final(UlongSlot.Reduction));

        // Every partial sum of ones is exact in float, so any order gives the same values
        AssertSameStep(floats, FloatSlot.Add, static s => s + 1f);
        AssertExchange(floats, FloatSlot.Exch, static t => (float)(t + 1));
        Assert.Equal(WarpSizedBlock * 0.5f, floats.Final(FloatSlot.Reduction));
    }

    // CUDA atomicInc / atomicDec: inc wraps to 0 once old >= limit; dec wraps to limit when old == 0 or old > limit
    private static uint Inc(uint old, uint limit) => old >= limit ? 0u : old + 1u;

    private static uint Dec(uint old, uint limit) => old == 0u || old > limit ? limit : old - 1u;

    // Every thread applies the same step: the old values are the first 32 states in some order, the final value the 33rd
    private static void AssertSameStep<T>(Slots<T> slots, int slot, Func<T, T> step)
    {
        var states = new T[WarpSizedBlock + 1];
        states[0] = slots.Initial(slot);
        for (var i = 1; i <= WarpSizedBlock; i++)
        {
            states[i] = step(states[i - 1]);
        }

        Assert.Equal(states[..^1].Order(), slots.SeenBy(slot).Order());
        Assert.Equal(states[^1], slots.Final(slot));
    }

    // Each operand is stored once: the old values and the final value are the initial value and the operands
    private static void AssertExchange<T>(Slots<T> slots, int slot, Func<int, T> operand) =>
        Assert.Equal(
            Operands(operand).Append(slots.Initial(slot)).Order(),
            slots.SeenBy(slot).Append(slots.Final(slot)).Order()
        );

    // From 0 to the operand: exactly one thread saw 0 and stored its operand, which every other thread saw
    private static void AssertCompareExchange<T>(Slots<T> slots, int slot, Func<int, T> operand)
        where T : INumberBase<T>
    {
        var seen = slots.SeenBy(slot);
        Assert.True(T.IsZero(slots.Initial(slot)));
        var first = Assert.Single(Enumerable.Range(0, WarpSizedBlock), t => T.IsZero(seen[t]));
        Assert.Equal(operand(first), slots.Final(slot));
        Assert.All(
            Enumerable.Range(0, WarpSizedBlock).Where(t => t != first),
            t => Assert.Equal(slots.Final(slot), seen[t])
        );
    }

    // Distinct operands: the final value is the least (the greatest) of the initial value and the operands, and no thread
    // saw its own operand (only that thread stores it), unless it is the initial value
    private static void AssertMin<T>(Slots<T> slots, int slot, Func<int, T> operand) where T : INumber<T> =>
        AssertExtreme(slots, slot, operand, Enumerable.Min);

    private static void AssertMax<T>(Slots<T> slots, int slot, Func<int, T> operand) where T : INumber<T> =>
        AssertExtreme(slots, slot, operand, Enumerable.Max);

    private static void AssertExtreme<T>(Slots<T> slots, int slot, Func<int, T> operand,
        Func<IEnumerable<T>, T> extreme) where T : INumber<T>
    {
        var initial = slots.Initial(slot);
        var seen = slots.SeenBy(slot);
        Assert.Equal(extreme(Operands(operand).Append(initial)), slots.Final(slot));
        Assert.All(
            Enumerable.Range(0, WarpSizedBlock).Where(t => operand(t) != initial),
            t => Assert.NotEqual(operand(t), seen[t])
        );
    }

    // Thread t changes only bit t: it saw that bit as it was initially, and the final value has every bit changed
    private static void AssertBits<T>(Slots<T> slots, int slot, Func<int, T> bit) where T : IBinaryInteger<T>
    {
        var initial = slots.Initial(slot);
        var seen = slots.SeenBy(slot);
        var all = T.Zero;
        for (var t = 0; t < WarpSizedBlock; t++)
        {
            Assert.Equal(initial & bit(t), seen[t] & bit(t));
            all |= bit(t);
        }

        Assert.Equal(initial ^ all, slots.Final(slot));
    }

    private static IEnumerable<T> Operands<T>(Func<int, T> operand) =>
        Enumerable.Range(0, WarpSizedBlock).Select(operand);

    // One element type of AtomicOldValueKernel: one address per slot, and the old value each thread got there
    private sealed class Slots<T>(int count)
    {
        private readonly T[] _initial = new T[count];

        public T[] Values { get; } = new T[count];

        public T[] Olds { get; } = new T[count * WarpSizedBlock];

        public T this[int slot]
        {
            set
            {
                _initial[slot] = value;
                Values[slot] = value;
            }
        }

        public T Initial(int slot) => _initial[slot];

        public T Final(int slot) => Values[slot];

        public T[] SeenBy(int slot) => Olds[(slot * WarpSizedBlock)..((slot + 1) * WarpSizedBlock)];
    }
}
