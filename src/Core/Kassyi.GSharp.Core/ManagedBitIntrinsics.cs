using System.Numerics;

using static Kassyi.GSharp.Core.ManagedIntrinsics;

namespace Kassyi.GSharp.Core;

/// <summary>
/// The managed CPU path of the bit counting, reversal, permutation, funnel shift, bit field and logic intrinsics
/// of <c>Gpu</c>, to which the stubs generated from <c>src/NativeApi/ptx_intrinsics.yml</c> forward through their
/// <c>managed</c> key.
/// </summary>
/// <remarks>
/// <para>
/// Each method gives the value that the CUDA intrinsic gives and that the native CPU prelude computes
/// (<c>MathAndBitOps.hpp</c>): integer arithmetic wraps as the PTX instruction does.
/// </para>
/// <para>Outside a managed launch each method throws <see cref="GpuOnlyException"/>, as the stub did.</para>
/// </remarks>
internal static class ManagedBitIntrinsics
{
    public static uint CountLeadingZeros(uint val) => Active((uint)BitOperations.LeadingZeroCount(val));

    public static uint CountLeadingZeros(ulong val) => Active((uint)BitOperations.LeadingZeroCount(val));

    // Byte n of the result is the byte of the 64-bit value {y, x} that the low three bits of nibble n of the selector
    // name (bytes 0-3 are x, 4-7 are y); the high bit of each nibble is ignored
    public static uint BytePermute(uint x, uint y, uint selector)
    {
        var pair = ((ulong)y << 32) | x;
        var result = 0u;
        for (var n = 0; n < 4; n++)
        {
            var index = (int)((selector >> (4 * n)) & 7u);
            result |= (uint)((pair >> (8 * index)) & 0xFFu) << (8 * n);
        }

        return Active(result);
    }

    public static uint PopCount(uint val) => Active((uint)BitOperations.PopCount(val));

    public static uint PopCount(ulong val) => Active((uint)BitOperations.PopCount(val));

    public static uint BitReverse(uint val)
    {
        var result = 0u;
        for (var i = 0; i < 32; i++)
        {
            result |= ((val >> i) & 1u) << (31 - i);
        }

        return Active(result);
    }

    public static ulong BitReverse(ulong val) =>
        Active(((ulong)BitReverse((uint)val) << 32) | BitReverse((uint)(val >> 32)));

    public static uint CountTrailingZeros(uint val) => Active((uint)BitOperations.TrailingZeroCount(val));

    public static uint CountTrailingZeros(ulong val) => Active((uint)BitOperations.TrailingZeroCount(val));

    // The high (left) or low (right) 32 bits of {hi, lo} shifted by shift; CUDA wraps the shift to 0-31
    public static uint FunnelShiftLeft(uint lo, uint hi, uint shift) =>
        Active((uint)(((((ulong)hi << 32) | lo) << (int)(shift & 31u)) >> 32));

    public static uint FunnelShiftRight(uint lo, uint hi, uint shift) =>
        Active((uint)((((ulong)hi << 32) | lo) >> (int)(shift & 31u)));

    // PTX bfe.u32 / bfe.u64: the bits of the field past the top of a are zero. The GPU takes pos and len modulo 256 for
    // 32 bits and as unsigned 32-bit values for 64 bits
    public static uint BitFieldExtract(uint a, int pos, int len) =>
        Active((uint)ExtractField(a, (uint)pos & 0xFFu, (uint)len & 0xFFu, 32));

    public static ulong BitFieldExtract(ulong a, int pos, int len) =>
        Active(ExtractField(a, (uint)pos, (uint)len, 64));

    // PTX bfi.b32 / bfi.b64: the low bits of a replace the field of b from pos, up to the top of b; pos and len as for
    // the extraction
    public static uint BitFieldInsert(uint a, uint b, int pos, int len) =>
        Active((uint)InsertField(a, b, (uint)pos & 0xFFu, (uint)len & 0xFFu, 32));

    public static ulong BitFieldInsert(ulong a, ulong b, int pos, int len) =>
        Active(InsertField(a, b, (uint)pos, (uint)len, 64));

    // PTX lop3.b32: bit i of the result is bit (a_i << 2 | b_i << 1 | c_i) of the truth table
    public static uint Lop3(uint a, uint b, uint c, byte immLut)
    {
        var result = 0u;
        for (var index = 0; index < 8; index++)
        {
            if (((immLut >> index) & 1) != 0)
            {
                result |= ((index & 4) != 0 ? a : ~a) & ((index & 2) != 0 ? b : ~b) & ((index & 1) != 0 ? c : ~c);
            }
        }

        return Active(result);
    }

    private static ulong ExtractField(ulong a, uint start, uint length, uint bits)
    {
        if (length == 0 || start >= bits)
        {
            return 0ul;
        }

        var field = a >> (int)start;
        return length >= bits ? field : field & ((1ul << (int)length) - 1ul);
    }

    private static ulong InsertField(ulong a, ulong b, uint start, uint length, uint bits)
    {
        if (length == 0 || start >= bits)
        {
            return b;
        }

        var width = (int)Math.Min(length, bits - start);
        var mask = width >= 64 ? ulong.MaxValue : (1ul << width) - 1ul;
        return (b & ~(mask << (int)start)) | ((a & mask) << (int)start);
    }
}
