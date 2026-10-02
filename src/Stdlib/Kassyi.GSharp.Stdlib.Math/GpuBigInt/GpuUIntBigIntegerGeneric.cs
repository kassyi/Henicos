namespace Kassyi.GSharp.Stdlib.Math.GpuBigInt;

/// <summary>
/// Represents a fixed-size arbitrary-precision unsigned integer stored in value-type memory on the GPU.
/// </summary>
/// <typeparam name="TSize">The unmanaged struct type representing the limb storage layout.</typeparam>
[StructLayout(LayoutKind.Sequential)]
public struct GpuUIntBigInteger<TSize> : IEquatable<GpuUIntBigInteger<TSize>>, IComparable<GpuUIntBigInteger<TSize>> where TSize : unmanaged
{
    /// <summary>
    /// The fixed-size limb storage backing this big integer instance.
    /// </summary>
    public TSize Limbs;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuUIntBigInteger{TSize}"/> struct with specified limbs.
    /// </summary>
    /// <param name="limbs">The limb storage payload.</param>
    // ReSharper disable once UnusedMember.Global (Public API for external consumers)
    public GpuUIntBigInteger(TSize limbs)
    {
        Limbs = limbs;
    }

    public static GpuUIntBigInteger<TSize> Zero => default;

    public static GpuUIntBigInteger<TSize> One
    {
        get
        {
            var result = default(GpuUIntBigInteger<TSize>);
            result.GetSpan()[0] = 1;
            return result;
        }
    }

    private readonly Span<uint> GetSpan()
    {
        ref var r = ref Unsafe.AsRef(in Limbs);
        return MemoryMarshal.CreateSpan(ref Unsafe.As<TSize, uint>(ref r), Unsafe.SizeOf<TSize>() / sizeof(uint));
    }

    public static GpuUIntBigInteger<TSize> operator +(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) =>
        left.Add(right, out _);

    public static GpuUIntBigInteger<TSize> operator -(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) =>
        left.Subtract(right, out _);

    public static bool operator >(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) =>
        left.CompareTo(right) > 0;

    public static bool operator <(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) =>
        left.CompareTo(right) < 0;

    public static bool operator >=(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) =>
        left.CompareTo(right) >= 0;

    public static bool operator <=(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) =>
        left.CompareTo(right) <= 0;

    public readonly GpuUIntBigInteger<TSize> Add(GpuUIntBigInteger<TSize> other, out bool carry)
    {
        var result = default(GpuUIntBigInteger<TSize>);
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

    public readonly GpuUIntBigInteger<TSize> Subtract(GpuUIntBigInteger<TSize> other, out bool borrow)
    {
        var result = default(GpuUIntBigInteger<TSize>);
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

    public readonly GpuUIntBigInteger<TSize> ShiftRight(int bits, out bool stickyBit) => ShiftRight(bits, out _, out _, out stickyBit);

    public readonly GpuUIntBigInteger<TSize> ShiftRight(int bits, out bool guardBit, out bool roundBit, out bool stickyBit)
    {
        guardBit = false;
        roundBit = false;

        if (bits == 0)
        {
            stickyBit = false;
            return this;
        }

        var result = default(GpuUIntBigInteger<TSize>);
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

    public readonly GpuUIntBigInteger<TSize> ShiftLeft(int bits)
    {
        if (bits == 0)
        {
            return this;
        }

        var result = default(GpuUIntBigInteger<TSize>);
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

    public GpuUIntBigInteger<TSize> SetMsb()
    {
        var result = this;
        var span = result.GetSpan();
        if (span.Length > 0)
        {
            span[^1] |= 0x80000000u;
        }
        return result;
    }

    public readonly bool IsOdd()
    {
        var span = GetSpan();
        return span.Length > 0 && (span[0] & 1u) != 0;
    }

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

    public readonly int CompareTo(GpuUIntBigInteger<TSize> other)
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

    public readonly bool Equals(GpuUIntBigInteger<TSize> other) => CompareTo(other) == 0;

    public readonly override bool Equals(object? obj) => obj is GpuUIntBigInteger<TSize> other && Equals(other);

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

    public static bool operator ==(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) => left.Equals(right);
    public static bool operator !=(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) => !left.Equals(right);
}

