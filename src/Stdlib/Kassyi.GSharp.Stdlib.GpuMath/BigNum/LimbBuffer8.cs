namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

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
