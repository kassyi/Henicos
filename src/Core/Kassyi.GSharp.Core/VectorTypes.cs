using System.Runtime.InteropServices;

// ReSharper disable UnusedMember.Global
#pragma warning disable IDE0051 // Remove unused private members

namespace Kassyi.GSharp.Core;

// --- Float ---
[StructLayout(LayoutKind.Sequential)]
public struct Float2(float x, float y)
{
    public float X = x; public float Y = y;
}

[StructLayout(LayoutKind.Sequential)]
public struct Float3(float x, float y, float z)
{
    public float X = x; public float Y = y; public float Z = z;
}

[StructLayout(LayoutKind.Sequential)]
public struct Float4(float x, float y, float z, float w)
{
    public float X = x; public float Y = y; public float Z = z; public float W = w;
}

// --- Int ---
[StructLayout(LayoutKind.Sequential)]
public struct Int2(int x, int y)
{
    public int X = x; public int Y = y;
}

[StructLayout(LayoutKind.Sequential)]
public struct Int3(int x, int y, int z)
{
    public int X = x; public int Y = y; public int Z = z;
}

[StructLayout(LayoutKind.Sequential)]
public struct Int4(int x, int y, int z, int w)
{
    public int X = x; public int Y = y; public int Z = z; public int W = w;
}

// --- UInt ---
[StructLayout(LayoutKind.Sequential)]
public struct UInt2(uint x, uint y)
{
    public uint X = x; public uint Y = y;
}

[StructLayout(LayoutKind.Sequential)]
public struct UInt3(uint x, uint y, uint z)
{
    public uint X = x; public uint Y = y; public uint Z = z;
}

[StructLayout(LayoutKind.Sequential)]
public struct UInt4(uint x, uint y, uint z, uint w)
{
    public uint X = x; public uint Y = y; public uint Z = z; public uint W = w;
}

// --- Double ---
[StructLayout(LayoutKind.Sequential)]
public struct Double2(double x, double y)
{
    public double X = x; public double Y = y;
}

[StructLayout(LayoutKind.Sequential)]
public struct Double3(double x, double y, double z)
{
    public double X = x; public double Y = y; public double Z = z;
}

[StructLayout(LayoutKind.Sequential)]
public struct Double4(double x, double y, double z, double w)
{
    public double X = x; public double Y = y; public double Z = z; public double W = w;
}

// --- Long ---
[StructLayout(LayoutKind.Sequential)]
public struct Long2(long x, long y)
{
    public long X = x; public long Y = y;
}

[StructLayout(LayoutKind.Sequential)]
public struct Long3(long x, long y, long z)
{
    public long X = x; public long Y = y; public long Z = z;
}

[StructLayout(LayoutKind.Sequential)]
public struct Long4(long x, long y, long z, long w)
{
    public long X = x; public long Y = y; public long Z = z; public long W = w;
}

// --- ULong ---
[StructLayout(LayoutKind.Sequential)]
public struct ULong2(ulong x, ulong y)
{
    public ulong X = x; public ulong Y = y;
}

[StructLayout(LayoutKind.Sequential)]
public struct ULong3(ulong x, ulong y, ulong z)
{
    public ulong X = x; public ulong Y = y; public ulong Z = z;
}

[StructLayout(LayoutKind.Sequential)]
public struct ULong4(ulong x, ulong y, ulong z, ulong w)
{
    public ulong X = x; public ulong Y = y; public ulong Z = z; public ulong W = w;
}

