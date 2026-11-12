namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

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
