using System.Diagnostics.Contracts;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A marker attribute instructing the GpuMath Source Generator to emit operator overloads and
/// expression tree dispatch logic for a given GPU number type.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class GenerateGpuNumberOperatorsAttribute : Attribute;

/// <summary>
/// The root contract for all GPU big number types (both fixed-width stack/register structs and arbitrary-precision device-backed structs).
/// </summary>
/// <remarks>
/// <para>
/// <b>Mathematical Model:</b> Every GPU number represents a value $(-1)^{\text{IsNegative}} \times \text{Magnitude} \times 2^{\text{Exponent}}$,
/// where unsigned types are simply numbers with <c>IsNegative == false</c> (<c>CanBeNegative == false</c>) and integers are numbers with <c>Exponent == 0</c>.
/// </para>
/// </remarks>
public interface IGpuBigNumberBase
{
    /// <summary>
    /// Gets a value indicating whether this number is negative. Always <see langword="false"/> for unsigned types.
    /// </summary>
    bool IsNegative { get; }

    /// <summary>
    /// Gets a value indicating whether this number type can represent negative values (<see langword="true"/> for signed types; <see langword="false"/> for unsigned types).
    /// </summary>
    bool CanBeNegative { get; }

    /// <summary>
    /// Determines whether the magnitude of this number is zero.
    /// </summary>
    /// <returns><see langword="true"/> if the number equals zero; otherwise, <see langword="false"/>.</returns>
    [Pure]
    bool IsZero();

    /// <summary>
    /// Calculates the minimum number of bits required to represent the magnitude of this number without leading zeros, or 0 if zero.
    /// </summary>
    /// <returns>The bit length of the magnitude, or 0 if zero.</returns>
    [Pure]
    int GetBitLength();
}

/// <summary>
/// Represents a self-typed numeric value executed on the GPU, supporting value equality and total ordering.
/// </summary>
/// <typeparam name="TSelf">The concrete value type implementing this interface.</typeparam>
public interface IGpuNumber<TSelf> : IGpuBigNumberBase, IEquatable<TSelf>, IComparable<TSelf>
    where TSelf : struct, IGpuNumber<TSelf>;

/// <summary>
/// Represents an arbitrary-precision GPU big number backed by a <see cref="GpuBigUInt"/> magnitude buffer in device memory,
/// unifying <see cref="GpuBigUInt"/>, <see cref="GpuBigInt"/>, <see cref="GpuBigUFloat"/>, and <see cref="GpuBigFloat"/> under a single type parameter.
/// </summary>
/// <typeparam name="TSelf">The concrete struct type implementing this interface.</typeparam>
public interface IGpuBigNumber<TSelf> : IGpuNumber<TSelf>, IDisposable
    where TSelf : struct, IGpuBigNumber<TSelf>
{
    /// <summary>
    /// Gets the <see cref="GpuWorkspace"/> managing device memory and stream execution for this number.
    /// </summary>
    GpuWorkspace Workspace { get; }

    /// <summary>
    /// Gets the unsigned magnitude (mantissa/significand) buffer stored in GPU device memory.
    /// </summary>
    GpuBigUInt Magnitude { get; }

    /// <summary>
    /// Gets the binary base-2 exponent ($E$ in $\text{Magnitude} \times 2^E$). Always 0 for integer types.
    /// </summary>
    int Exponent { get; }

    /// <summary>
    /// Gets the effective binary scale ($\text{Exponent} + \text{GetBitLength}()$) of a non-zero number.
    /// </summary>
    long EffectiveScale { get; }

    /// <summary>
    /// Creates a new instance of <typeparamref name="TSelf"/> from its unified mathematical components.
    /// </summary>
    /// <param name="magnitude">The unsigned magnitude buffer.</param>
    /// <param name="exponent">The base-2 exponent (must be 0 for integer types).</param>
    /// <param name="isNegative">The sign flag (must be <see langword="false"/> for unsigned types unless magnitude is zero).</param>
    /// <returns>A new <typeparamref name="TSelf"/> instance.</returns>
    static abstract TSelf Create(GpuBigUInt magnitude, int exponent, bool isNegative);
}

/// <summary>
/// Provides unified, zero-allocation algorithms shared across all <see cref="IGpuBigNumber{TSelf}"/> types.
/// </summary>
public static class GpuBigNumberAlgorithms
{
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
    /// Pairs an unsigned <see cref="GpuBigUInt"/> magnitude with its precomputed exact bit length.
    /// </summary>
    /// <param name="Magnitude">The unsigned magnitude buffer.</param>
    /// <param name="BitLength">The exact bit length of <paramref name="Magnitude"/>.</param>
    public readonly record struct BitSizedMagnitude(GpuBigUInt Magnitude, int BitLength);

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
}
