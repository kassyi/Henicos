namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A shared dummy enumerator struct to satisfy the C# 12 <see cref="CollectionBuilderAttribute"/> element type requirement (CS9188).
/// </summary>
/// <remarks>
/// <para><b>Execution Target:</b> Host compile-time contract support for collection expressions. Never allocated or iterated at runtime.</para>
/// </remarks>
[ExcludeFromCodeCoverage]
public struct DummyUIntEnumerator
{
    /// <summary>
    /// Gets a default element value of 0.
    /// </summary>
    public uint Current => 0;

    /// <summary>
    /// Always returns <see langword="false"/> to indicate an empty sequence.
    /// </summary>
    /// <returns><see langword="false"/> unconditionally.</returns>
    public bool MoveNext() => false;
}

/// <summary>
/// A 128-bit (4 limbs $\times$ 32-bit words) zero-allocation inline array buffer used for <see cref="GpuUInt128"/>.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Backed by C# 12 <see cref="InlineArrayAttribute"/>, allocating contiguous stack/device memory without heap overhead.</para>
/// </remarks>
[InlineArray(4)]
public struct LimbBuffer4
{
    private uint _element0;
}

/// <summary>
/// A 256-bit (8 limbs $\times$ 32-bit words) zero-allocation inline array buffer used for <see cref="GpuUInt256"/>.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Backed by C# 12 <see cref="InlineArrayAttribute"/>, allocating contiguous stack/device memory without heap overhead.</para>
/// </remarks>
[InlineArray(8)]
public struct LimbBuffer8
{
    private uint _element0;
}

/// <summary>
/// A 512-bit (16 limbs $\times$ 32-bit words) zero-allocation inline array buffer used for <see cref="GpuUInt512"/>.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Backed by C# 12 <see cref="InlineArrayAttribute"/>, allocating contiguous stack/device memory without heap overhead.</para>
/// </remarks>
[InlineArray(16)]
public struct LimbBuffer16
{
    private uint _element0;
}
