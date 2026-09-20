namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Represents a 256-bit unsigned integer stored on the GPU.
/// </summary>
[CollectionBuilder(typeof(GpuUInt256), nameof(Create))]
public struct GpuUInt256
{
    public GpuUIntBigInteger<LimbBuffer8> Value;

    public GpuUInt256(GpuUIntBigInteger<LimbBuffer8> value) => Value = value;
    public GpuUInt256(uint value) => Value = GpuUIntBuilder.Create<LimbBuffer8>([value]);

    public static implicit operator GpuUIntBigInteger<LimbBuffer8>(GpuUInt256 val) => val.Value;
    public static implicit operator GpuUInt256(GpuUIntBigInteger<LimbBuffer8> val) => new GpuUInt256(val);
    public static implicit operator GpuUInt256(uint val) => new GpuUInt256(val);

    // Dummy enumerator to satisfy C# 12 CollectionBuilder element type requirement (CS9188)
    public Enumerator GetEnumerator() => default;
    public struct Enumerator
    {
        public uint Current => 0; public bool MoveNext() => false;
    }

    public static GpuUInt256 Create(ReadOnlySpan<uint> elements) => new GpuUInt256(GpuUIntBuilder.Create<LimbBuffer8>(elements));
}
