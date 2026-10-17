using System.Buffers;
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Provides unified, zero-allocation algorithms shared across all <see cref="IGpuBigNumber{TSelf}"/> types.
/// </summary>
public static class GpuBigNumberAlgorithms
{
    private readonly ref struct VirtualShiftedLimbReader(ReadOnlySpan<uint> words, int shiftBits)
    {
        private readonly ReadOnlySpan<uint> _words = words;
        private readonly int _wordShift = shiftBits / 32;
        private readonly int _bitShift = shiftBits % 32;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetWord(int virtualWordIndex)
        {
            var srcIdx = virtualWordIndex - _wordShift;
            var high = (uint)srcIdx < (uint)_words.Length ? _words[srcIdx] : 0u;
            if (_bitShift == 0)
            {
                return high;
            }

            var lowIdx = srcIdx - 1;
            var low = (uint)lowIdx < (uint)_words.Length ? _words[lowIdx] : 0u;
            return (high << _bitShift) | (low >> (32 - _bitShift));
        }
    }

    /// <summary>
    /// Compares two arbitrary-precision GPU numbers of type <typeparamref name="T"/> without allocating temporary GPU buffers or executing subtraction kernels.
    /// </summary>
    /// <typeparam name="T">The concrete GPU big number type.</typeparam>
    /// <param name="left">The first number to compare.</param>
    /// <param name="right">The second number to compare.</param>
    /// <returns>Less than zero if <paramref name="left"/> &lt; <paramref name="right"/>; zero if equal; greater than zero if <paramref name="left"/> &gt; <paramref name="right"/>.</returns>
    public static int Compare<T>(T left, T right)
        where T : struct, IGpuBigNumber<T>
    {
        // Fast path when exponents match (always true for GpuBigUInt and GpuBigInt, and common for aligned floats)
        if (left.Exponent == right.Exponent)
        {
            if (!left.CanBeNegative && !right.CanBeNegative)
            {
                return left.Magnitude.CompareTo(right.Magnitude);
            }

            if (left.IsNegative == right.IsNegative)
            {
                var absCmp = left.Magnitude.CompareTo(right.Magnitude);
                return left.IsNegative ? -absCmp : absCmp;
            }

            if (left.IsZero() && right.IsZero())
            {
                return 0;
            }

            return left.IsNegative ? -1 : 1;
        }

        // General path for differing exponents (GpuBigUFloat / GpuBigFloat)
        var leftBits = left.GetBitLength();
        var rightBits = right.GetBitLength();
        var leftZero = leftBits == 0;
        var rightZero = rightBits == 0;

        if (leftZero && rightZero)
        {
            return 0;
        }

        if (leftZero)
        {
            return right.IsNegative ? 1 : -1;
        }

        if (rightZero)
        {
            return left.IsNegative ? -1 : 1;
        }

        if (left.IsNegative != right.IsNegative)
        {
            return left.IsNegative ? -1 : 1;
        }

        var sign = left.IsNegative ? -1 : 1;

        // O(1) Scale-first check: Value lies in [2^(Exponent + Bits - 1), 2^(Exponent + Bits))
        var leftScale = (long)left.Exponent + leftBits;
        var rightScale = (long)right.Exponent + rightBits;
        if (leftScale != rightScale)
        {
            return leftScale > rightScale ? sign : -sign;
        }

        // Same effective scale: compare significands aligned at their most significant bit
        var magCmp = CompareAlignedMagnitudes(
            new BitSizedMagnitude(left.Magnitude, leftBits),
            new BitSizedMagnitude(right.Magnitude, rightBits));
        return left.IsNegative ? -magCmp : magCmp;
    }

    /// <summary>
    /// Compares two non-zero <see cref="GpuBigUInt"/> magnitudes with their most significant bits virtually aligned.
    /// </summary>
    /// <param name="left">The left magnitude and its exact bit length.</param>
    /// <param name="right">The right magnitude and its exact bit length.</param>
    /// <returns>A signed integer indicating the relative order of the virtually aligned magnitudes.</returns>
    public static int CompareAlignedMagnitudes(BitSizedMagnitude left, BitSizedMagnitude right)
    {
        if (left.BitLength == right.BitLength)
        {
            return left.Magnitude.CompareTo(right.Magnitude);
        }

        var maxBits = Math.Max(left.BitLength, right.BitLength);
        var leftShift = maxBits - left.BitLength;
        var rightShift = maxBits - right.BitLength;

        var leftActiveWords = (left.BitLength + 31) / 32;
        var rightActiveWords = (right.BitLength + 31) / 32;
        var maxVirtualWords = (maxBits + 31) / 32;

        var leftHost = ArrayPool<uint>.Shared.Rent(leftActiveWords);
        var rightHost = ArrayPool<uint>.Shared.Rent(rightActiveWords);
        try
        {
            left.Magnitude.Workspace.Provider.CopyDeviceToHost(
                leftHost, left.Magnitude.DevicePointer, leftActiveWords, left.Magnitude.Workspace.Stream);
            right.Magnitude.Workspace.Provider.CopyDeviceToHost(
                rightHost, right.Magnitude.DevicePointer, rightActiveWords, right.Magnitude.Workspace.Stream);

            var leftReader = new VirtualShiftedLimbReader(leftHost.AsSpan(0, leftActiveWords), leftShift);
            var rightReader = new VirtualShiftedLimbReader(rightHost.AsSpan(0, rightActiveWords), rightShift);

            for (var i = maxVirtualWords - 1; i >= 0; i--)
            {
                var wordA = leftReader.GetWord(i);
                var wordB = rightReader.GetWord(i);
                if (wordA != wordB)
                {
                    return wordA > wordB ? 1 : -1;
                }
            }

            return 0;
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(leftHost);
            ArrayPool<uint>.Shared.Return(rightHost);
        }
    }
}
