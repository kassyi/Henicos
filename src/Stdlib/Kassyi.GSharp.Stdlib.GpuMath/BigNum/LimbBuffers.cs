namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

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
