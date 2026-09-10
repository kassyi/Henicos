namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Represents a fixed-size arbitrary-precision unsigned integer stored in value-type memory on the GPU.
/// </summary>
/// <typeparam name="TSize">The unmanaged struct type representing the limb storage layout.</typeparam>
[StructLayout(LayoutKind.Sequential)]
public struct GpuUIntBigInteger<TSize> where TSize : unmanaged
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

    public readonly GpuUIntBigInteger<TSize> ShiftRight(int bits, out bool stickyBit)
    {
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
            sticky |= src[i];
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

            sticky |= carry;
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
}

