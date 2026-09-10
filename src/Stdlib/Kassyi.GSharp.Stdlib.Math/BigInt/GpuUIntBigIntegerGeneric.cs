using System.Runtime.InteropServices;

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

    public static GpuUIntBigInteger<TSize> operator +(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) => default;
    public static GpuUIntBigInteger<TSize> operator -(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) => default;
    public static bool operator >(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) => false;
    public static bool operator <(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) => false;
    public static bool operator >=(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) => true;
    public static bool operator <=(GpuUIntBigInteger<TSize> left, GpuUIntBigInteger<TSize> right) => true;

    public readonly GpuUIntBigInteger<TSize> Add(GpuUIntBigInteger<TSize> other, out bool carry) { carry = false; return default; }
    public readonly GpuUIntBigInteger<TSize> Subtract(GpuUIntBigInteger<TSize> other, out bool borrow) { borrow = false; return default; }
    public readonly GpuUIntBigInteger<TSize> ShiftRight(int bits, out bool stickyBit) { stickyBit = false; return default; }
    public readonly GpuUIntBigInteger<TSize> ShiftLeft(int bits) => default;
    
    public readonly int CountLeadingZeros() => 0;
    public readonly bool IsZero() => false;
    public readonly int CompareTo(GpuUIntBigInteger<TSize> other) => 0;
}
