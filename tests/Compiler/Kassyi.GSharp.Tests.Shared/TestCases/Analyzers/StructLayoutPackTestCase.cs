using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public struct StructWithoutPack
{
    public int A;
    public double B;
}

[StructLayout(LayoutKind.Sequential, Pack = 0)]
public struct StructWithPackZero
{
    public int A;
    public double B;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct StructWithPackFour
{
    public int A;
    public double B;
}
