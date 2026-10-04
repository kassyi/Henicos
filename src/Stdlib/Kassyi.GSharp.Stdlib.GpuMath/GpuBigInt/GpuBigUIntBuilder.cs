namespace Kassyi.GSharp.Stdlib.GpuMath.BigInt;

/// <summary>
/// A shared generic builder for C# 12 Collection Expressions.
/// </summary>
public static class GpuBigUIntBuilder
{
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
