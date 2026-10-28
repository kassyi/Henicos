using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.DeviceLibrary;

/// <summary>A pair of integers, defined on the device from metadata alone (fields only).</summary>
public struct Pair
{
    public int A;
    public int B;
}

/// <summary>
/// Two integers in the reverse of the declaration order. [StructLayout] and [FieldOffset] are pseudo-attributes, which
/// a consumer reads from the metadata tables, not from the custom attributes (#736).
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct SwapPair
{
    [FieldOffset(4)]
    public int A;

    [FieldOffset(0)]
    public int B;
}

/// <summary>A byte and an integer without padding (5 bytes, from Pack = 1 in the metadata).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PackedPair
{
    public byte Tag;
    public int Value;
}

/// <summary>One integer in 16 bytes (StructLayout.Size in the metadata): array elements sit 16 bytes apart (#736).</summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct Sized
{
    public int A;
}

/// <summary>An integer and a float that share their bytes, and a tag after them (8 bytes, #736).</summary>
[StructLayout(LayoutKind.Explicit)]
public struct IntFloatUnion
{
    [FieldOffset(0)]
    public int Bits;

    [FieldOffset(0)]
    public float Value;

    [FieldOffset(4)]
    public int Tag;
}

/// <summary>A vector with a constructor and a computed property, which the library's records bring to a consumer.</summary>
public struct Vec2
{
    public float X;
    public float Y;

    // Not an aggregate initialization: a consumer that skipped the body would get Sum = x + y
    public Vec2(float x, float y)
    {
        X = x * 2;
        Y = y + 1;
    }

    public float Sum => X + Y;
}

/// <summary>A particle at a Core vector position, which no G# assembly records (#538 review B1).</summary>
public struct Particle
{
    public Float3 Position;
    public float Mass;

    public Particle(Float3 position, float mass)
    {
        Position = position;
        Mass = mass * 2f;
    }

    public float Weight => Mass + Position.X;
}

/// <summary>A constructor that calls a device function of the library, which the consumer declares first (M1).</summary>
public struct Scaled
{
    public int V;

    public Scaled(int v)
    {
        V = LibMath.Twice(v);
    }
}

/// <summary>A field of another struct, and a computed property that names Zeta only through its constructor (M1).</summary>
public struct Alpha
{
    public Omega Inner;

    public Alpha(int v)
    {
        Inner = new Omega(v);
    }

    public int Probe => new Zeta(Inner.V).V;
}

public struct Omega
{
    public int V;

    public Omega(int v)
    {
        V = v + 1;
    }
}

public struct Zeta
{
    public int V;

    public Zeta(int v)
    {
        V = v * 3;
    }
}

/// <summary>Device functions that the kernels of another assembly call.</summary>
public static partial class LibMath
{
    /// <summary>The Montgomery constant <c>-n0^-1 mod 2^32</c> (the shape of <c>CooperativeMath.ComputeMontgomeryMu</c>).</summary>
    [GpuDeviceFunction]
    public static uint Mu(uint n0)
    {
        uint inv = 1u;
        for (int i = 0; i < 5; i++)
        {
            inv *= 2u - n0 * inv;
        }

        return (uint)-(int)inv;
    }

    [GpuDeviceFunction]
    public static int SumPair(Pair pair) => pair.A + pair.B;

    /// <summary>Calls an internal device function, which the consumer imports with it.</summary>
    [GpuDeviceFunction]
    public static int Twice(int x) => Helper(x) * 2;

    [GpuDeviceFunction]
    internal static int Helper(int x) => x;

    /// <summary>A Core vector parameter.</summary>
    [GpuDeviceFunction]
    public static float Dot3(Float3 v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z;

    /// <summary>Uses Float3 inside the library only.</summary>
    [GpuDeviceFunction]
    public static float Length3(float x, float y, float z) => Dot3(new Float3(x, y, z));

    /// <summary>Writes the value, waits for the block, and reads the next thread's value (two barriers).</summary>
    [GpuDeviceFunction]
    public static int Exchange(int[] buffer, int tid, int value)
    {
        buffer[tid] = value;
        Gpu.SyncThreads();
        int next = buffer[(tid + 1) % BlockDimension.X];
        Gpu.SyncThreads();
        return next;
    }
}
