namespace Kassyi.GSharp.Tests.DeviceLibrary;

/// <summary>
/// A converted struct (it holds an array) with internal fields: a launcher generated in another assembly cannot name
/// them and copies them through the host mirror.
/// </summary>
public struct LibInternalFieldHolder
{
    public int[] Data;
    internal int Hidden;
    internal long Wide;

    public LibInternalFieldHolder(int[] data, int hidden, long wide)
    {
        Data = data;
        Hidden = hidden;
        Wide = wide;
    }

    public readonly int HiddenValue => Hidden;

    public readonly long WideValue => Wide;
}
