namespace Kassyi.GSharp.Tests.DeviceLibrary;

/// <summary>
/// A struct with a property whose setter uses the <c>field</c> keyword and an auto-property: a consumer reads the
/// accessors from the library's record, since the metadata has no bodies.
/// </summary>
public struct LibScaled
{
    public int X { get; set => field = value * 2; }

    public int Y { get; set; }
}
