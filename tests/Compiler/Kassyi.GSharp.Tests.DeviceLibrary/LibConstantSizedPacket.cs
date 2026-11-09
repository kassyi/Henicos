namespace Kassyi.GSharp.Tests.DeviceLibrary;

/// <summary>
/// A struct with only fields, whose <c>fixed</c> buffer is sized by a constant: a consumer defines it on the device
/// from metadata, without its syntax.
/// </summary>
public unsafe struct LibConstantSizedPacket
{
    public const int Size = 3;

    public int Head;
    public fixed int Items[Size];
    public int Tail;
}
