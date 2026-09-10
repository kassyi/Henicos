namespace Kassyi.GSharp.Stdlib.Math.BigInt;

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
