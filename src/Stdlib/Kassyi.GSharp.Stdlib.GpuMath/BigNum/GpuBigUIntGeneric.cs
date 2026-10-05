namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents a fixed-size arbitrary-precision unsigned integer stored in value-type memory on the GPU.
/// </summary>
/// <typeparam name="TSize">The unmanaged struct type representing the limb storage layout (e.g., <see cref="LimbBuffer4"/>, <see cref="LimbBuffer8"/>, <see cref="LimbBuffer16"/>).</typeparam>
/// <remarks>
/// <para><b>Performance:</b> Zero-allocation stack/register/device-memory value type backed by a contiguous fixed-size unmanaged struct.</para>
/// <para><b>Execution Target:</b> Usable in both host and GPU device execution environments.</para>
/// <para><b>Thread Safety:</b> Value-type semantics; concurrent reads of distinct instances are thread-safe.</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct GpuBigUInt<TSize> : IEquatable<GpuBigUInt<TSize>>, IComparable<GpuBigUInt<TSize>> where TSize : unmanaged
{
    /// <summary>
    /// The fixed-size limb storage backing this big integer instance.
    /// </summary>
    public TSize Limbs;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigUInt{TSize}"/> struct with the specified limb storage.
    /// </summary>
    /// <param name="limbs">The limb storage payload.</param>
    // ReSharper disable once UnusedMember.Global (Public API for external consumers)
    public GpuBigUInt(TSize limbs)
    {
        Limbs = limbs;
    }

    /// <summary>
    /// Gets a value that represents the number 0 (zero).
    /// </summary>
    public static GpuBigUInt<TSize> Zero => default;

    /// <summary>
    /// Gets a value that represents the number 1 (one).
    /// </summary>
    public static GpuBigUInt<TSize> One
    {
        get
        {
            var result = default(GpuBigUInt<TSize>);
            result.GetSpan()[0] = 1;
            return result;
        }
    }

    // [WHY] Span projection from unmanaged struct reference avoids heap allocations and allows direct indexed limb access.
    private readonly Span<uint> GetSpan()
    {
        ref var r = ref Unsafe.AsRef(in Limbs);
        return MemoryMarshal.CreateSpan(ref Unsafe.As<TSize, uint>(ref r), Unsafe.SizeOf<TSize>() / sizeof(uint));
    }

    /// <summary>
    /// Adds two specified <see cref="GpuBigUInt{TSize}"/> values.
    /// </summary>
    /// <param name="left">The first value to add.</param>
    /// <param name="right">The second value to add.</param>
    /// <returns>The sum of <paramref name="left"/> and <paramref name="right"/>.</returns>
    public static GpuBigUInt<TSize> operator +(GpuBigUInt<TSize> left, GpuBigUInt<TSize> right) =>
        left.Add(right, out _);

    /// <summary>
    /// Subtracts a <see cref="GpuBigUInt{TSize}"/> value from another <see cref="GpuBigUInt{TSize}"/> value.
    /// </summary>
    /// <param name="left">The value to subtract from (minuend).</param>
    /// <param name="right">The value to subtract (subtrahend).</param>
    /// <returns>The result of subtracting <paramref name="right"/> from <paramref name="left"/>.</returns>
    public static GpuBigUInt<TSize> operator -(GpuBigUInt<TSize> left, GpuBigUInt<TSize> right) =>
        left.Subtract(right, out _);

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigUInt{TSize}"/> is greater than another specified <see cref="GpuBigUInt{TSize}"/>.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >(GpuBigUInt<TSize> left, GpuBigUInt<TSize> right) =>
        left.CompareTo(right) > 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigUInt{TSize}"/> is less than another specified <see cref="GpuBigUInt{TSize}"/>.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <(GpuBigUInt<TSize> left, GpuBigUInt<TSize> right) =>
        left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigUInt{TSize}"/> is greater than or equal to another specified <see cref="GpuBigUInt{TSize}"/>.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is greater than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator >=(GpuBigUInt<TSize> left, GpuBigUInt<TSize> right) =>
        left.CompareTo(right) >= 0;

    /// <summary>
    /// Determines whether one specified <see cref="GpuBigUInt{TSize}"/> is less than or equal to another specified <see cref="GpuBigUInt{TSize}"/>.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is less than or equal to <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator <=(GpuBigUInt<TSize> left, GpuBigUInt<TSize> right) =>
        left.CompareTo(right) <= 0;

    /// <summary>
    /// Adds another <see cref="GpuBigUInt{TSize}"/> to this instance, returning the sum and reporting carry-out.
    /// </summary>
    /// <param name="other">The value to add.</param>
    /// <param name="carry">When this method returns, contains <see langword="true"/> if an arithmetic carry out of the most significant limb occurred; otherwise, <see langword="false"/>.</param>
    /// <returns>The sum of this instance and <paramref name="other"/>.</returns>
    public readonly GpuBigUInt<TSize> Add(GpuBigUInt<TSize> other, out bool carry)
    {
        var result = default(GpuBigUInt<TSize>);
        var leftSpan = GetSpan();
        var rightSpan = other.GetSpan();
        var resSpan = result.GetSpan();

        ulong c = 0;
        for (var i = 0; i < leftSpan.Length; i++)
        {
            var sum = leftSpan[i] + (ulong)rightSpan[i] + c;
            resSpan[i] = (uint)sum;
            c = sum >> 32;
        }

        carry = c != 0;
        return result;
    }

    /// <summary>
    /// Subtracts another <see cref="GpuBigUInt{TSize}"/> from this instance, returning the difference and reporting borrow-out.
    /// </summary>
    /// <param name="other">The value to subtract.</param>
    /// <param name="borrow">When this method returns, contains <see langword="true"/> if an arithmetic borrow out of the most significant limb occurred; otherwise, <see langword="false"/>.</param>
    /// <returns>The difference of this instance minus <paramref name="other"/>.</returns>
    public readonly GpuBigUInt<TSize> Subtract(GpuBigUInt<TSize> other, out bool borrow)
    {
        var result = default(GpuBigUInt<TSize>);
        var leftSpan = GetSpan();
        var rightSpan = other.GetSpan();
        var resSpan = result.GetSpan();

        long b = 0;
        for (var i = 0; i < leftSpan.Length; i++)
        {
            var diff = leftSpan[i] - (long)rightSpan[i] - b;
            if (diff < 0)
            {
                diff += 0x1_0000_0000L;
                b = 1;
            }
            else
            {
                b = 0;
            }

            resSpan[i] = (uint)diff;
        }

        borrow = b != 0;
        return result;
    }

    /// <summary>
    /// Shifts the big integer to the right by the specified number of bits, reporting whether any discarded lower bits were set (sticky bit).
    /// </summary>
    /// <param name="bits">The number of bits to shift right.</param>
    /// <param name="stickyBit">When this method returns, contains <see langword="true"/> if any bit shifted out of the least significant limb was non-zero; otherwise, <see langword="false"/>.</param>
    /// <returns>The right-shifted value.</returns>
    public readonly GpuBigUInt<TSize> ShiftRight(int bits, out bool stickyBit) => ShiftRight(bits, out _, out _, out stickyBit);

    /// <summary>
    /// Shifts the big integer to the right by the specified number of bits, extracting IEEE 754 rounding components: Guard bit, Round bit, and Sticky bit.
    /// </summary>
    /// <param name="bits">The number of bits to shift right.</param>
    /// <param name="guardBit">When this method returns, contains <see langword="true"/> if the first bit shifted out (most significant discarded bit) was 1; otherwise, <see langword="false"/>.</param>
    /// <param name="roundBit">When this method returns, contains <see langword="true"/> if the second bit shifted out was 1; otherwise, <see langword="false"/>.</param>
    /// <param name="stickyBit">When this method returns, contains <see langword="true"/> if any remaining shifted-out bits beyond the round bit were 1; otherwise, <see langword="false"/>.</param>
    /// <returns>The right-shifted value.</returns>
    public readonly GpuBigUInt<TSize> ShiftRight(int bits, out bool guardBit, out bool roundBit, out bool stickyBit)
    {
        guardBit = false;
        roundBit = false;

        if (bits == 0)
        {
            stickyBit = false;
            return this;
        }

        var result = default(GpuBigUInt<TSize>);
        var src = GetSpan();
        var dst = result.GetSpan();

        var limbShift = bits / 32;
        var bitShift = bits % 32;

        uint sticky = 0;
        for (var i = 0; i < limbShift && i < src.Length; i++)
        {
            if (i == limbShift - 1 && bitShift == 0)
            {
                guardBit = (src[i] & 0x8000_0000u) != 0;
                roundBit = (src[i] & 0x4000_0000u) != 0;
                sticky |= (src[i] & 0x3FFF_FFFFu);
            }
            else
            {
                sticky |= src[i];
            }
        }

        if (limbShift >= src.Length)
        {
            stickyBit = sticky != 0;
            return result;
        }

        if (bitShift == 0)
        {
            for (var i = limbShift; i < src.Length; i++)
            {
                dst[i - limbShift] = src[i];
            }
        }
        else
        {
            uint carry = 0;
            for (var i = src.Length - 1; i >= limbShift; i--)
            {
                var val = src[i];
                dst[i - limbShift] = (val >> bitShift) | carry;
                carry = val << (32 - bitShift);
            }

            guardBit = (carry & 0x8000_0000u) != 0;
            roundBit = (carry & 0x4000_0000u) != 0;
            sticky |= (carry & 0x3FFF_FFFFu);
        }

        stickyBit = sticky != 0;
        return result;
    }

    /// <summary>
    /// Shifts the big integer to the left by the specified number of bits.
    /// </summary>
    /// <param name="bits">The number of bits to shift left.</param>
    /// <returns>The left-shifted value.</returns>
    public readonly GpuBigUInt<TSize> ShiftLeft(int bits)
    {
        if (bits == 0)
        {
            return this;
        }

        var result = default(GpuBigUInt<TSize>);
        var src = GetSpan();
        var dst = result.GetSpan();

        var limbShift = bits / 32;
        var bitShift = bits % 32;

        if (limbShift >= src.Length)
        {
            return result;
        }

        if (bitShift == 0)
        {
            for (var i = 0; i < src.Length - limbShift; i++)
            {
                dst[i + limbShift] = src[i];
            }
        }
        else
        {
            uint carry = 0;
            for (var i = 0; i < src.Length - limbShift; i++)
            {
                var val = src[i];
                dst[i + limbShift] = (val << bitShift) | carry;
                carry = val >> (32 - bitShift);
            }
        }

        return result;
    }

    /// <summary>
    /// Sets the most significant bit (MSB) of the most significant limb.
    /// </summary>
    /// <returns>A copy of this instance with the most significant bit set.</returns>
    public GpuBigUInt<TSize> SetMsb()
    {
        var result = this;
        var span = result.GetSpan();
        if (span.Length > 0)
        {
            span[^1] |= 0x80000000u;
        }
        return result;
    }

    /// <summary>
    /// Determines whether the value represents an odd integer.
    /// </summary>
    /// <returns><see langword="true"/> if the least significant bit is 1; otherwise, <see langword="false"/>.</returns>
    public readonly bool IsOdd()
    {
        var span = GetSpan();
        return span.Length > 0 && (span[0] & 1u) != 0;
    }

    /// <summary>
    /// Counts the number of leading zero bits in the big integer starting from the most significant bit.
    /// </summary>
    /// <returns>The total count of leading zero bits.</returns>
    public readonly int CountLeadingZeros()
    {
        var span = GetSpan();
        var count = 0;
        for (var i = span.Length - 1; i >= 0; i--)
        {
            if (span[i] == 0)
            {
                count += 32;
            }
            else
            {
                count += BitOperations.LeadingZeroCount(span[i]);
                break;
            }
        }

        return count;
    }

    /// <summary>
    /// Determines whether all limbs in this instance are zero.
    /// </summary>
    /// <returns><see langword="true"/> if all limb words equal 0; otherwise, <see langword="false"/>.</returns>
    public readonly bool IsZero()
    {
        var span = GetSpan();
        foreach (var t in span)
        {
            if (t != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Compares this instance to another <see cref="GpuBigUInt{TSize}"/> and returns an integer indicating their relative order.
    /// </summary>
    /// <param name="other">The <see cref="GpuBigUInt{TSize}"/> to compare against.</param>
    /// <returns>
    /// A signed integer that indicates the relative values of this instance and <paramref name="other"/>:
    /// Less than zero if this instance is less than <paramref name="other"/>,
    /// Zero if this instance equals <paramref name="other"/>,
    /// Greater than zero if this instance is greater than <paramref name="other"/>.
    /// </returns>
    public readonly int CompareTo(GpuBigUInt<TSize> other)
    {
        var span = GetSpan();
        var otherSpan = other.GetSpan();
        for (var i = span.Length - 1; i >= 0; i--)
        {
            if (span[i] > otherSpan[i])
            {
                return 1;
            }

            if (span[i] < otherSpan[i])
            {
                return -1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Indicates whether the current instance is equal to another <see cref="GpuBigUInt{TSize}"/> instance.
    /// </summary>
    /// <param name="other">A <see cref="GpuBigUInt{TSize}"/> to compare with this instance.</param>
    /// <returns><see langword="true"/> if the current instance is equal to the <paramref name="other"/> parameter; otherwise, <see langword="false"/>.</returns>
    public readonly bool Equals(GpuBigUInt<TSize> other) => CompareTo(other) == 0;

    /// <summary>
    /// Determines whether the specified object is equal to the current instance.
    /// </summary>
    /// <param name="obj">The object to compare with the current instance.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is a <see cref="GpuBigUInt{TSize}"/> and equals the current instance; otherwise, <see langword="false"/>.</returns>
    public readonly override bool Equals(object? obj) => obj is GpuBigUInt<TSize> other && Equals(other);

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public readonly override int GetHashCode()
    {
        var span = GetSpan();
        var hash = new HashCode();
        foreach (var t in span)
        {
            hash.Add(t);
        }
        return hash.ToHashCode();
    }

    /// <summary>
    /// Determines whether two specified <see cref="GpuBigUInt{TSize}"/> instances are equal.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> equals <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(GpuBigUInt<TSize> left, GpuBigUInt<TSize> right) => left.Equals(right);

    /// <summary>
    /// Determines whether two specified <see cref="GpuBigUInt{TSize}"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> does not equal <paramref name="right"/>; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(GpuBigUInt<TSize> left, GpuBigUInt<TSize> right) => !left.Equals(right);
}

