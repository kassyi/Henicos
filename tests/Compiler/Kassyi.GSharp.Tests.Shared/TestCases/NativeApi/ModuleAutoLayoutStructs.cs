using System.Runtime.InteropServices;

namespace MyApp.Native;

[StructLayout(LayoutKind.Auto)]
public struct AutoPair
{
    public byte A;
    public double B;
}

[StructLayout(LayoutKind.Sequential)]
public struct SeqPair
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Explicit)]
public struct ExplicitPair
{
    [FieldOffset(0)]
    public int First;

    [FieldOffset(4)]
    public int Second;
}

public struct HoldsAuto
{
    public int Before;
    public AutoPair Inner;
}

public sealed class UserClass
{
    public int Value;
}
