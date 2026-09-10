using Kassyi.GSharp.Stdlib.Math.BigInt;

namespace Kassyi.GSharp.Stdlib.Math.BigFloat;

/// <summary>
/// Represents a 128-bit arbitrary-precision floating-point number stored on the GPU.
/// </summary>
public struct GpuFloat128
{
    public GpuBigFloat<LimbBuffer4> Value;

    public GpuFloat128(GpuBigFloat<LimbBuffer4> value)
    {
        Value = value;
    }

    public GpuFloat128(GpuUInt128 significand, int exponent, bool isNegative)
    {
        Value = new GpuBigFloat<LimbBuffer4>(significand.Value, exponent, isNegative);
    }

    public GpuFloat128(GpuBigUFloat<LimbBuffer4> uFloat, bool isNegative)
    {
        Value = new GpuBigFloat<LimbBuffer4>(uFloat, isNegative);
    }

    public static implicit operator GpuBigFloat<LimbBuffer4>(GpuFloat128 val) => val.Value;
    public static implicit operator GpuFloat128(GpuBigFloat<LimbBuffer4> val) => new GpuFloat128(val);

    public static GpuFloat128 operator +(GpuFloat128 left, GpuFloat128 right) => new GpuFloat128(left.Value + right.Value);
    public static GpuFloat128 operator -(GpuFloat128 left, GpuFloat128 right) => new GpuFloat128(left.Value - right.Value);
}
