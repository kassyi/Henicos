
namespace Kassyi.GSharp.Stdlib.Math.BigFloat;

/// <summary>
/// Represents a 512-bit arbitrary-precision floating-point number stored on the GPU.
/// </summary>
public struct GpuFloat512
{
    public GpuBigFloat<LimbBuffer16> Value;

    public GpuFloat512(GpuBigFloat<LimbBuffer16> value)
    {
        Value = value;
    }

    public GpuFloat512(GpuUInt512 significand, int exponent, bool isNegative)
    {
        Value = new GpuBigFloat<LimbBuffer16>(significand.Value, exponent, isNegative);
    }

    public GpuFloat512(GpuBigUFloat<LimbBuffer16> uFloat, bool isNegative)
    {
        Value = new GpuBigFloat<LimbBuffer16>(uFloat, isNegative);
    }

    public static implicit operator GpuBigFloat<LimbBuffer16>(GpuFloat512 val) => val.Value;
    public static implicit operator GpuFloat512(GpuBigFloat<LimbBuffer16> val) => new GpuFloat512(val);

    public static GpuFloat512 operator +(GpuFloat512 left, GpuFloat512 right) => new GpuFloat512(left.Value + right.Value);
    public static GpuFloat512 operator -(GpuFloat512 left, GpuFloat512 right) => new GpuFloat512(left.Value - right.Value);
}
