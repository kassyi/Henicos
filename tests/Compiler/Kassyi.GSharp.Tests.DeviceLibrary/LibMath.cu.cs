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
/// Two integers in the reverse of the declaration order. A consumer cannot read [StructLayout] and [FieldOffset] of a
/// type from metadata (Roslyn keeps them as pseudo-attributes), so it predicts A at 0 and B at 4.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct SwapPair
{
    [FieldOffset(4)]
    public int A;

    [FieldOffset(0)]
    public int B;
}

/// <summary>A byte and an integer without padding (5 bytes); a consumer predicts the sequential 8 bytes.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PackedPair
{
    public byte Tag;
    public int Value;
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

/// <summary>The counter that <see cref="LibValue"/>'s operator macro takes as its context.</summary>
public struct LibCounter
{
    public int Calls;
}

/// <summary>A value whose <c>+</c> is an operator macro of the library.</summary>
public struct LibValue
{
    public int V;

    public LibValue(int v) => V = v;

    [GpuOperatorMacro(typeof(LibMath), nameof(LibMath.AddValues))]
    public static LibValue operator +(LibValue left, LibValue right) => throw new GpuOnlyException();
}

/// <summary>A value whose <c>-</c> is an operator macro with a private target of the library.</summary>
public struct LibSecretValue
{
    public int V;

    public LibSecretValue(int v) => V = v;

    [GpuOperatorMacro(typeof(LibSecretOps), "Difference")]
    public static LibSecretValue operator -(LibSecretValue left, LibSecretValue right) => throw new GpuOnlyException();
}

/// <summary>Holds the private target of <see cref="LibSecretValue"/>'s <c>-</c>.</summary>
public static partial class LibSecretOps
{
    [GpuDeviceFunction]
    private static void Difference(ref LibSecretValue result, LibSecretValue left, LibSecretValue right)
    {
        result.V = left.V - right.V;
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

    /// <summary>The target of <see cref="LibValue"/>'s <c>+</c>.</summary>
    [GpuDeviceFunction]
    public static void AddValues(ref LibCounter counter, ref LibValue result, LibValue left, LibValue right)
    {
        result.V = left.V + right.V;
        counter.Calls = counter.Calls + 1;
    }

    /// <summary>
    /// Adds the pair through the operator macro (#711): the library emits the managed version that a consumer's managed
    /// fallback calls.
    /// </summary>
    [GpuDeviceFunction]
    public static int AddPair(ref LibCounter counter, Pair pair)
    {
        LibValue a = new LibValue(pair.A);
        LibValue b = new LibValue(pair.B);
        LibValue c = new LibValue(0);
        c = a + b;
        return c.V;
    }

    /// <summary>
    /// Subtracts through an operator macro whose target is private to another class of the library: the library's managed
    /// version calls the target through an [UnsafeAccessor] stub (#711).
    /// </summary>
    [GpuDeviceFunction]
    public static int Difference(Pair pair)
    {
        LibSecretValue a = new LibSecretValue(pair.A);
        LibSecretValue b = new LibSecretValue(pair.B);
        LibSecretValue c = new LibSecretValue(0);
        c = a - b;
        return c.V;
    }

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
