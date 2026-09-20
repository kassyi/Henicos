namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Represents a 128-bit unsigned integer stored on the GPU.
/// </summary>
[CollectionBuilder(typeof(GpuUInt128), nameof(Create))]
public struct GpuUInt128
{
    public GpuUIntBigInteger<LimbBuffer4> Value;

    public GpuUInt128(GpuUIntBigInteger<LimbBuffer4> value) => Value = value;
    public GpuUInt128(uint value) => Value = GpuUIntBuilder.Create<LimbBuffer4>([value]);

    public static implicit operator GpuUIntBigInteger<LimbBuffer4>(GpuUInt128 val) => val.Value;
    public static implicit operator GpuUInt128(GpuUIntBigInteger<LimbBuffer4> val) => new GpuUInt128(val);
    public static implicit operator GpuUInt128(uint val) => new GpuUInt128(val);

    // Dummy enumerator to satisfy C# 12 CollectionBuilder element type requirement (CS9188)
    public Enumerator GetEnumerator() => default;
    public struct Enumerator
    {
        public uint Current => 0; public bool MoveNext() => false;
    }

    public static GpuUInt128 Create(ReadOnlySpan<uint> elements) => new GpuUInt128(GpuUIntBuilder.Create<LimbBuffer4>(elements));
}
