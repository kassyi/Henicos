namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A factory builder facilitating C# 12 Collection Expression construction for <see cref="GpuBigUInt{TSize}"/>.
/// </summary>
/// <remarks>
/// <para>This builder is invoked by the compiler when initializing fixed-size GPU unsigned integers via collection expressions (e.g. <c>[1u, 2u]</c>).</para>
/// <para><b>Thread Safety:</b> Stateless static factory method; completely thread-safe.</para>
/// </remarks>
public static class GpuBigUIntBuilder
{
    /// <summary>
    /// Creates a new <see cref="GpuBigUInt{TSize}"/> instance initialized from a span of 32-bit unsigned limbs.
    /// </summary>
    /// <typeparam name="TSize">The unmanaged struct type representing the limb storage layout.</typeparam>
    /// <param name="elements">A read-only span containing the 32-bit unsigned limbs in little-endian order.</param>
    /// <returns>A new <see cref="GpuBigUInt{TSize}"/> instance containing the specified limbs.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="elements"/> length exceeds the capacity of <typeparamref name="TSize"/>.</exception>
    public static GpuBigUInt<TSize> Create<TSize>(ReadOnlySpan<uint> elements) where TSize : unmanaged
    {
        var maxLength = Unsafe.SizeOf<TSize>() / sizeof(uint);

        if (elements.Length > maxLength)
        {
            throw new ArgumentException(
                $"The collection expression provided {elements.Length} elements, but the target fixed-size buffer can only hold {maxLength} elements.");
        }

        TSize limbs = default;
        var span = MemoryMarshal.CreateSpan(ref Unsafe.As<TSize, uint>(ref limbs), maxLength);
        elements.CopyTo(span);

        return new GpuBigUInt<TSize>(limbs);
    }
}

