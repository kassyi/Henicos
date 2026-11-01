using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Kassyi.GSharp.Core;

/// <summary>
/// The managed CPU path of the <c>Gpu.Atomic*</c> and <c>Gpu.ReductionAdd</c> intrinsics (#728). The stubs generated
/// from <c>src/NativeApi/ptx_intrinsics.yml</c> forward here through their <c>managed</c> key.
/// </summary>
/// <remarks>
/// <para>
/// Each method is one atomic read-modify-write of the address and returns the value the address held before it, like
/// CUDA's <c>atomicXxx</c> and the native CPU prelude (<c>Atomics.hpp</c>, relaxed <c>std::atomic</c>). Integer
/// arithmetic wraps; <c>Inc</c> / <c>Dec</c> wrap at the limit as CUDA defines; a float is compared bit for bit, as
/// <c>compare_exchange</c> compares it.
/// </para>
/// <para>
/// The managed launcher steps the threads of a launch one at a time, so the operations on one address run in that
/// order, which is one of the orders the GPU may pick. <see cref="Interlocked"/> keeps each operation atomic also
/// against a launch that another host thread runs at the same time.
/// </para>
/// <para>Outside a managed launch each method throws <see cref="GpuOnlyException"/>, as the stub did.</para>
/// </remarks>
internal static class ManagedAtomics
{
    public static int Add(ref int address, int val) => unchecked(Interlocked.Add(ref Active(ref address), val) - val);

    public static uint Add(ref uint address, uint val) => unchecked(Interlocked.Add(ref Active(ref address), val) - val);

    public static ulong Add(ref ulong address, ulong val) =>
        unchecked(Interlocked.Add(ref Active(ref address), val) - val);

    public static float Add(ref float address, float val) => UpdateSingle(ref Active(ref address), val);

    public static int Sub(ref int address, int val) =>
        unchecked(Interlocked.Add(ref Active(ref address), -val) + val);

    public static uint Sub(ref uint address, uint val) =>
        unchecked(Interlocked.Add(ref Active(ref address), 0u - val) + val);

    public static int Exch(ref int address, int val) => Interlocked.Exchange(ref Active(ref address), val);

    public static uint Exch(ref uint address, uint val) => Interlocked.Exchange(ref Active(ref address), val);

    public static ulong Exch(ref ulong address, ulong val) => Interlocked.Exchange(ref Active(ref address), val);

    public static float Exch(ref float address, float val) => Interlocked.Exchange(ref Active(ref address), val);

    // CUDA's atomicCAS(address, compare, val) is Interlocked.CompareExchange(address, val, compare)
    public static int Cas(ref int address, int compare, int val) =>
        Interlocked.CompareExchange(ref Active(ref address), val, compare);

    public static uint Cas(ref uint address, uint compare, uint val) =>
        Interlocked.CompareExchange(ref Active(ref address), val, compare);

    public static ulong Cas(ref ulong address, ulong compare, ulong val) =>
        Interlocked.CompareExchange(ref Active(ref address), val, compare);

    public static int And(ref int address, int val) => Interlocked.And(ref Active(ref address), val);

    public static uint And(ref uint address, uint val) => Interlocked.And(ref Active(ref address), val);

    public static ulong And(ref ulong address, ulong val) => Interlocked.And(ref Active(ref address), val);

    public static uint Or(ref uint address, uint val) => Interlocked.Or(ref Active(ref address), val);

    public static ulong Or(ref ulong address, ulong val) => Interlocked.Or(ref Active(ref address), val);

    public static int Xor(ref int address, int val) => Update(ref Active(ref address), val, static (old, v) => old ^ v);

    public static uint Xor(ref uint address, uint val) =>
        Update(ref Active(ref address), val, static (old, v) => old ^ v);

    public static ulong Xor(ref ulong address, ulong val) =>
        Update(ref Active(ref address), val, static (old, v) => old ^ v);

    public static int Min(ref int address, int val) => Update(ref Active(ref address), val, int.Min);

    public static uint Min(ref uint address, uint val) => Update(ref Active(ref address), val, uint.Min);

    public static ulong Min(ref ulong address, ulong val) => Update(ref Active(ref address), val, ulong.Min);

    public static uint Max(ref uint address, uint val) => Update(ref Active(ref address), val, uint.Max);

    public static ulong Max(ref ulong address, ulong val) => Update(ref Active(ref address), val, ulong.Max);

    // atomicInc: old >= limit ? 0 : old + 1
    public static uint Inc(ref uint address, uint limit) =>
        Update(ref Active(ref address), limit, static (old, l) => old >= l ? 0u : old + 1u);

    // atomicDec: (old == 0 || old > limit) ? limit : old - 1
    public static uint Dec(ref uint address, uint limit) =>
        Update(ref Active(ref address), limit, static (old, l) => old == 0u || old > l ? l : old - 1u);

    // The address itself, after checking that a managed launch is running
    private static ref T Active<T>(ref T address)
    {
        if (!ManagedGpuContext.IsActive)
        {
            GpuOnlyException.Throw();
        }

        return ref address;
    }

    // A compare-and-swap loop, as __gsharp_atomic_update / __gsharp_atomic_min / __gsharp_atomic_max of the prelude
    private static T Update<T>(ref T address, T val, Func<T, T, T> update) where T : unmanaged, IBinaryInteger<T>
    {
        var old = address;
        while (true)
        {
            var seen = Interlocked.CompareExchange(ref address, update(old, val), old);
            if (seen == old)
            {
                return old;
            }

            old = seen;
        }
    }

    private const uint CanonicalNaNBits = 0x7FFFFFFFu;
    private const uint AbsMask = 0x7FFFFFFFu;
    private const uint SignMask = 0x80000000u;
    private const uint MinNormalBits = 0x00800000u;
    private const uint PositiveInfinityBits = 0x7F800000u;

    // A float add through the bits of the address, so that the loop ends also for a NaN or a signed zero, which a float
    // comparison would not tell from the value it read. Matches PTX atom.add.f32 / red.add.f32 (#868): on global memory,
    // subnormal inputs and results flush to sign-preserving zero; on shared memory, subnormals are preserved; on both, a
    // NaN result stores canonical NaN (0x7FFFFFFF), and the returned old value keeps the original un-flushed bits.
    private static float UpdateSingle(ref float address, float val)
    {
        var flush = !ManagedGpuContext.IsSharedMemoryAddress(ref address);
        var rhs = BitConverter.UInt32BitsToSingle(FlushSubnormal(BitConverter.SingleToUInt32Bits(val), flush));
        ref var bits = ref Unsafe.As<float, int>(ref address);
        var old = Volatile.Read(ref bits);
        while (true)
        {
            var lhs = BitConverter.UInt32BitsToSingle(FlushSubnormal((uint)old, flush));
            var sumBits = BitConverter.SingleToUInt32Bits(lhs + rhs);
            var nextBits = (sumBits & AbsMask) > PositiveInfinityBits ? CanonicalNaNBits : FlushSubnormal(sumBits, flush);

            var seen = Interlocked.CompareExchange(ref bits, unchecked((int)nextBits), old);
            if (seen == old)
            {
                return BitConverter.Int32BitsToSingle(old);
            }

            old = seen;
        }
    }

    // The bits of a float, with a subnormal turned into the zero of its sign when flush is set
    private static uint FlushSubnormal(uint bits, bool flush)
    {
        var magnitude = bits & AbsMask;
        return flush && magnitude != 0u && magnitude < MinNormalBits ? bits & SignMask : bits;
    }
}
