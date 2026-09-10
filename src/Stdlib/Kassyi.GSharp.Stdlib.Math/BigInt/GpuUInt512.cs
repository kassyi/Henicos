namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Represents a 512-bit unsigned integer stored on the GPU.
/// </summary>
[CollectionBuilder(typeof(GpuUInt512), nameof(Create))]
public struct GpuUInt512
{
    public GpuUIntBigInteger<LimbBuffer16> Value;

    public GpuUInt512(GpuUIntBigInteger<LimbBuffer16> value) => Value = value;
    public GpuUInt512(uint value) => Value = GpuUIntBuilder.Create<LimbBuffer16>([value]);

    public static implicit operator GpuUIntBigInteger<LimbBuffer16>(GpuUInt512 val) => val.Value;
    public static implicit operator GpuUInt512(GpuUIntBigInteger<LimbBuffer16> val) => new GpuUInt512(val);
    public static implicit operator GpuUInt512(uint val) => new GpuUInt512(val);

    // Dummy enumerator to satisfy C# 12 CollectionBuilder element type requirement (CS9188)
    public Enumerator GetEnumerator() => default;
    public struct Enumerator { public uint Current => 0; public bool MoveNext() => false; }

    public static GpuUInt512 Create(ReadOnlySpan<uint> elements) => new GpuUInt512(GpuUIntBuilder.Create<LimbBuffer16>(elements));
}
