using System.Runtime.InteropServices;

// ReSharper disable UnusedMember.Global
#pragma warning disable IDE0051 // Remove unused private members

namespace Kassyi.GSharp.Core;

// --- Float ---
[StructLayout(LayoutKind.Sequential)]
public struct Float2
{
    public float X; public float Y;
    public Float2(float x, float y)
    {
        X = x; Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct Float3
{
    public float X; public float Y; public float Z;
    public Float3(float x, float y, float z)
    {
        X = x; Y = y; Z = z;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct Float4
{
    public float X; public float Y; public float Z; public float W;
    public Float4(float x, float y, float z, float w)
    {
        X = x; Y = y; Z = z; W = w;
    }
}

// --- Int ---
[StructLayout(LayoutKind.Sequential)]
public struct Int2
{
    public int X; public int Y;
    public Int2(int x, int y)
    {
        X = x; Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct Int3
{
    public int X; public int Y; public int Z;
    public Int3(int x, int y, int z)
    {
        X = x; Y = y; Z = z;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct Int4
{
    public int X; public int Y; public int Z; public int W;
    public Int4(int x, int y, int z, int w)
    {
        X = x; Y = y; Z = z; W = w;
    }
}

// --- UInt ---
[StructLayout(LayoutKind.Sequential)]
public struct UInt2
{
    public uint X; public uint Y;
    public UInt2(uint x, uint y)
    {
        X = x; Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct UInt3
{
    public uint X; public uint Y; public uint Z;
    public UInt3(uint x, uint y, uint z)
    {
        X = x; Y = y; Z = z;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct UInt4
{
    public uint X; public uint Y; public uint Z; public uint W;
    public UInt4(uint x, uint y, uint z, uint w)
    {
        X = x; Y = y; Z = z; W = w;
    }
}

// --- Double ---
[StructLayout(LayoutKind.Sequential)]
public struct Double2
{
    public double X; public double Y;
    public Double2(double x, double y)
    {
        X = x; Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct Double3
{
    public double X; public double Y; public double Z;
    public Double3(double x, double y, double z)
    {
        X = x; Y = y; Z = z;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct Double4
{
    public double X; public double Y; public double Z; public double W;
    public Double4(double x, double y, double z, double w)
    {
        X = x; Y = y; Z = z; W = w;
    }
}

// --- Long ---
[StructLayout(LayoutKind.Sequential)]
public struct Long2
{
    public long X; public long Y;
    public Long2(long x, long y)
    {
        X = x; Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct Long3
{
    public long X; public long Y; public long Z;
    public Long3(long x, long y, long z)
    {
        X = x; Y = y; Z = z;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct Long4
{
    public long X; public long Y; public long Z; public long W;
    public Long4(long x, long y, long z, long w)
    {
        X = x; Y = y; Z = z; W = w;
    }
}

// --- ULong ---
[StructLayout(LayoutKind.Sequential)]
public struct ULong2
{
    public ulong X; public ulong Y;
    public ULong2(ulong x, ulong y)
    {
        X = x; Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct ULong3
{
    public ulong X; public ulong Y; public ulong Z;
    public ULong3(ulong x, ulong y, ulong z)
    {
        X = x; Y = y; Z = z;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct ULong4
{
    public ulong X; public ulong Y; public ulong Z; public ulong W;
    public ULong4(ulong x, ulong y, ulong z, ulong w)
    {
        X = x; Y = y; Z = z; W = w;
    }
}

