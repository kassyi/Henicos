using static Kassyi.GSharp.Core.ManagedIntrinsics;

namespace Kassyi.GSharp.Core;

/// <summary>
/// The managed CPU path of the wide multiplication, carry chain, absolute difference and byte dot product
/// intrinsics of <c>Gpu</c>, to which the stubs generated from <c>src/NativeApi/ptx_intrinsics.yml</c> forward through
/// their <c>managed</c> key.
/// </summary>
/// <remarks>
/// <para>
/// Each method gives the value that the CUDA intrinsic gives and that the native CPU prelude computes
/// (<c>MathAndBitOps.hpp</c>): integer arithmetic wraps as the PTX instruction does.
/// </para>
/// <para>Outside a managed launch each method throws <see cref="GpuOnlyException"/>, as the stub did.</para>
/// </remarks>
internal static class ManagedIntegerIntrinsics
{
    public static ulong MultiplyWide(uint a, uint b) => Active((ulong)a * b);

    public static ulong MultiplyHigh(ulong a, ulong b) => Active(Math.BigMul(a, b, out _));

    public static int MultiplyHigh(int a, int b) => Active((int)(((long)a * b) >> 32));

    public static long MultiplyHigh(long a, long b) => Active(Math.BigMul(a, b, out _));

    // |a - b| in 32 bits, as PTX sad: a signed difference above int.MaxValue keeps its 32 bits and reads as negative
    public static int AbsDiff(int a, int b) => Active(unchecked((int)(a < b ? (uint)b - (uint)a : (uint)a - (uint)b)));

    public static uint AbsDiff(uint a, uint b) => Active(a < b ? b - a : a - b);

    public static ulong MultiplyAddWide(uint a, uint b, ulong c) => Active(unchecked((ulong)a * b + c));

    public static long MultiplyAddWide(int a, int b, long c) => Active(unchecked((long)a * b + c));

    // (a * b) + c + carry-in; the low bit of carry is the carry-in, and carry receives the carry-out of the 64-bit sum
    public static ulong MultiplyAddWideWithCarry(uint a, uint b, ulong c, ref ulong carry)
    {
        Active(0);
        var product = (ulong)a * b;
        var partial = unchecked(product + c);
        var carryOut = partial < product ? 1ul : 0ul;
        var result = unchecked(partial + (carry & 1ul));
        carryOut |= result < partial ? 1ul : 0ul;
        carry = carryOut;
        return result;
    }

    // PTX add.cc / addc: any non-zero low 32 bits of the carry-in add one; carry receives 0 or 1
    public static uint AddWithCarry(uint a, uint b, ref ulong carry)
    {
        Active(0);
        var sum = (ulong)a + b + ((uint)carry != 0u ? 1ul : 0ul);
        carry = sum >> 32;
        return (uint)sum;
    }

    // PTX sub.cc / subc: any non-zero low 32 bits of the borrow-in subtract one; borrow receives 0 or 1
    public static uint SubWithBorrow(uint a, uint b, ref long borrow)
    {
        Active(0);
        var subtrahend = (ulong)b + ((uint)borrow != 0u ? 1ul : 0ul);
        borrow = a < subtrahend ? 1L : 0L;
        return unchecked((uint)(a - subtrahend));
    }

    // __dp4a: c plus the products of the four byte pairs, signed or unsigned, wrapping in 32 bits
    public static int DotProduct4(int a, int b, int c)
    {
        var sum = c;
        for (var shift = 0; shift < 32; shift += 8)
        {
            sum = unchecked(sum + (sbyte)(a >> shift) * (sbyte)(b >> shift));
        }

        return Active(sum);
    }

    public static uint DotProduct4(uint a, uint b, uint c)
    {
        var sum = c;
        for (var shift = 0; shift < 32; shift += 8)
        {
            sum = unchecked(sum + (byte)(a >> shift) * (uint)(byte)(b >> shift));
        }

        return Active(sum);
    }

    // PTX vabsdiff4.add: c plus the absolute differences of the four unsigned byte pairs, wrapping in 32 bits
    public static uint Sad4(uint a, uint b, uint c)
    {
        var sum = c;
        for (var shift = 0; shift < 32; shift += 8)
        {
            sum = unchecked(sum + (uint)Math.Abs((byte)(a >> shift) - (byte)(b >> shift)));
        }

        return Active(sum);
    }
}
