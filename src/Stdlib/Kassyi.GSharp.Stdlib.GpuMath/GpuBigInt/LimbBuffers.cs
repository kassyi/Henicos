namespace Kassyi.GSharp.Stdlib.GpuMath.BigInt;

/// <summary>
/// A shared dummy enumerator to satisfy C# 12 CollectionBuilder element type requirement (CS9188).
/// </summary>
public struct DummyUIntEnumerator
{
    public uint Current => 0;
    public bool MoveNext() => false;
}

/// <summary>
/// A 128-bit (4 limbs * 32-bit) zero-allocation inline array buffer.
/// </summary>
[InlineArray(4)]
public struct LimbBuffer4
{
    private uint _element0;
}

/// <summary>
/// A 256-bit (8 limbs * 32-bit) zero-allocation inline array buffer.
/// </summary>
[InlineArray(8)]
public struct LimbBuffer8
{
    private uint _element0;
}

/// <summary>
/// A 512-bit (16 limbs * 32-bit) zero-allocation inline array buffer.
/// </summary>
[InlineArray(16)]
public struct LimbBuffer16
{
    private uint _element0;
}
