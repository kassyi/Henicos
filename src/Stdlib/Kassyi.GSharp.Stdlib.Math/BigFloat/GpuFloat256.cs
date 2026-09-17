
namespace Kassyi.GSharp.Stdlib.Math.BigFloat;

/// <summary>
/// Represents a 256-bit arbitrary-precision floating-point number stored on the GPU.
/// </summary>
public struct GpuFloat256
{
    public GpuBigFloat<LimbBuffer8> Value;

    public GpuFloat256(GpuBigFloat<LimbBuffer8> value)
    {
        Value = value;
    }

    public GpuFloat256(GpuUInt256 significand, int exponent, bool isNegative)
    {
        Value = new GpuBigFloat<LimbBuffer8>(significand.Value, exponent, isNegative);
    }

    public GpuFloat256(GpuBigUFloat<LimbBuffer8> uFloat, bool isNegative)
    {
        Value = new GpuBigFloat<LimbBuffer8>(uFloat, isNegative);
    }

    public static implicit operator GpuBigFloat<LimbBuffer8>(GpuFloat256 val) => val.Value;
    public static implicit operator GpuFloat256(GpuBigFloat<LimbBuffer8> val) => new GpuFloat256(val);

    public static GpuFloat256 operator +(GpuFloat256 left, GpuFloat256 right) => new GpuFloat256(left.Value + right.Value);
    public static GpuFloat256 operator -(GpuFloat256 left, GpuFloat256 right) => new GpuFloat256(left.Value - right.Value);
}
