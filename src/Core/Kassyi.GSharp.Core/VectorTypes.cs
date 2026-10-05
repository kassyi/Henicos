using System.Runtime.InteropServices;

// ReSharper disable UnusedMember.Global
#pragma warning disable IDE0051 // Remove unused private members

namespace Kassyi.GSharp.Core;

// --- Float ---

/// <summary>
/// Represents a 2-component vector of single-precision floating-point values corresponding to the CUDA native <c>float2</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 8 bytes, 8-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Float2(float x, float y)
{
    /// <summary>The X component of the vector.</summary>
    public float X = x;

    /// <summary>The Y component of the vector.</summary>
    public float Y = y;
}

/// <summary>
/// Represents a 3-component vector of single-precision floating-point values corresponding to the CUDA native <c>float3</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <param name="z">The Z component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 12 bytes, 4-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Float3(float x, float y, float z)
{
    /// <summary>The X component of the vector.</summary>
    public float X = x;

    /// <summary>The Y component of the vector.</summary>
    public float Y = y;

    /// <summary>The Z component of the vector.</summary>
    public float Z = z;
}

/// <summary>
/// Represents a 4-component vector of single-precision floating-point values corresponding to the CUDA native <c>float4</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <param name="z">The Z component of the vector.</param>
/// <param name="w">The W component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 16 bytes, 16-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Float4(float x, float y, float z, float w)
{
    /// <summary>The X component of the vector.</summary>
    public float X = x;

    /// <summary>The Y component of the vector.</summary>
    public float Y = y;

    /// <summary>The Z component of the vector.</summary>
    public float Z = z;

    /// <summary>The W component of the vector.</summary>
    public float W = w;
}

// --- Int ---

/// <summary>
/// Represents a 2-component vector of 32-bit signed integer values corresponding to the CUDA native <c>int2</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 8 bytes, 8-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Int2(int x, int y)
{
    /// <summary>The X component of the vector.</summary>
    public int X = x;

    /// <summary>The Y component of the vector.</summary>
    public int Y = y;
}

/// <summary>
/// Represents a 3-component vector of 32-bit signed integer values corresponding to the CUDA native <c>int3</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <param name="z">The Z component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 12 bytes, 4-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Int3(int x, int y, int z)
{
    /// <summary>The X component of the vector.</summary>
    public int X = x;

    /// <summary>The Y component of the vector.</summary>
    public int Y = y;

    /// <summary>The Z component of the vector.</summary>
    public int Z = z;
}

/// <summary>
/// Represents a 4-component vector of 32-bit signed integer values corresponding to the CUDA native <c>int4</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <param name="z">The Z component of the vector.</param>
/// <param name="w">The W component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 16 bytes, 16-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Int4(int x, int y, int z, int w)
{
    /// <summary>The X component of the vector.</summary>
    public int X = x;

    /// <summary>The Y component of the vector.</summary>
    public int Y = y;

    /// <summary>The Z component of the vector.</summary>
    public int Z = z;

    /// <summary>The W component of the vector.</summary>
    public int W = w;
}

// --- UInt ---

/// <summary>
/// Represents a 2-component vector of 32-bit unsigned integer values corresponding to the CUDA native <c>uint2</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 8 bytes, 8-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct UInt2(uint x, uint y)
{
    /// <summary>The X component of the vector.</summary>
    public uint X = x;

    /// <summary>The Y component of the vector.</summary>
    public uint Y = y;
}

/// <summary>
/// Represents a 3-component vector of 32-bit unsigned integer values corresponding to the CUDA native <c>uint3</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <param name="z">The Z component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 12 bytes, 4-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct UInt3(uint x, uint y, uint z)
{
    /// <summary>The X component of the vector.</summary>
    public uint X = x;

    /// <summary>The Y component of the vector.</summary>
    public uint Y = y;

    /// <summary>The Z component of the vector.</summary>
    public uint Z = z;
}

/// <summary>
/// Represents a 4-component vector of 32-bit unsigned integer values corresponding to the CUDA native <c>uint4</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <param name="z">The Z component of the vector.</param>
/// <param name="w">The W component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 16 bytes, 16-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct UInt4(uint x, uint y, uint z, uint w)
{
    /// <summary>The X component of the vector.</summary>
    public uint X = x;

    /// <summary>The Y component of the vector.</summary>
    public uint Y = y;

    /// <summary>The Z component of the vector.</summary>
    public uint Z = z;

    /// <summary>The W component of the vector.</summary>
    public uint W = w;
}

// --- Double ---

/// <summary>
/// Represents a 2-component vector of double-precision floating-point values corresponding to the CUDA native <c>double2</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 16 bytes, 16-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Double2(double x, double y)
{
    /// <summary>The X component of the vector.</summary>
    public double X = x;

    /// <summary>The Y component of the vector.</summary>
    public double Y = y;
}

/// <summary>
/// Represents a 3-component vector of double-precision floating-point values corresponding to the CUDA native <c>double3</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <param name="z">The Z component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 24 bytes, 8-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Double3(double x, double y, double z)
{
    /// <summary>The X component of the vector.</summary>
    public double X = x;

    /// <summary>The Y component of the vector.</summary>
    public double Y = y;

    /// <summary>The Z component of the vector.</summary>
    public double Z = z;
}

/// <summary>
/// Represents a 4-component vector of double-precision floating-point values corresponding to the CUDA native <c>double4</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <param name="z">The Z component of the vector.</param>
/// <param name="w">The W component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 32 bytes, 16-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Double4(double x, double y, double z, double w)
{
    /// <summary>The X component of the vector.</summary>
    public double X = x;

    /// <summary>The Y component of the vector.</summary>
    public double Y = y;

    /// <summary>The Z component of the vector.</summary>
    public double Z = z;

    /// <summary>The W component of the vector.</summary>
    public double W = w;
}

// --- Long ---

/// <summary>
/// Represents a 2-component vector of 64-bit signed integer values corresponding to the CUDA native <c>long2</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 16 bytes, 16-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Long2(long x, long y)
{
    /// <summary>The X component of the vector.</summary>
    public long X = x;

    /// <summary>The Y component of the vector.</summary>
    public long Y = y;
}

/// <summary>
/// Represents a 3-component vector of 64-bit signed integer values corresponding to the CUDA native <c>long3</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <param name="z">The Z component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 24 bytes, 8-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Long3(long x, long y, long z)
{
    /// <summary>The X component of the vector.</summary>
    public long X = x;

    /// <summary>The Y component of the vector.</summary>
    public long Y = y;

    /// <summary>The Z component of the vector.</summary>
    public long Z = z;
}

/// <summary>
/// Represents a 4-component vector of 64-bit signed integer values corresponding to the CUDA native <c>long4</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <param name="z">The Z component of the vector.</param>
/// <param name="w">The W component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 32 bytes, 16-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Long4(long x, long y, long z, long w)
{
    /// <summary>The X component of the vector.</summary>
    public long X = x;

    /// <summary>The Y component of the vector.</summary>
    public long Y = y;

    /// <summary>The Z component of the vector.</summary>
    public long Z = z;

    /// <summary>The W component of the vector.</summary>
    public long W = w;
}

// --- ULong ---

/// <summary>
/// Represents a 2-component vector of 64-bit unsigned integer values corresponding to the CUDA native <c>ulong2</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 16 bytes, 16-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct ULong2(ulong x, ulong y)
{
    /// <summary>The X component of the vector.</summary>
    public ulong X = x;

    /// <summary>The Y component of the vector.</summary>
    public ulong Y = y;
}

/// <summary>
/// Represents a 3-component vector of 64-bit unsigned integer values corresponding to the CUDA native <c>ulong3</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <param name="z">The Z component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 24 bytes, 8-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct ULong3(ulong x, ulong y, ulong z)
{
    /// <summary>The X component of the vector.</summary>
    public ulong X = x;

    /// <summary>The Y component of the vector.</summary>
    public ulong Y = y;

    /// <summary>The Z component of the vector.</summary>
    public ulong Z = z;
}

/// <summary>
/// Represents a 4-component vector of 64-bit unsigned integer values corresponding to the CUDA native <c>ulong4</c> type.
/// </summary>
/// <param name="x">The X component of the vector.</param>
/// <param name="y">The Y component of the vector.</param>
/// <param name="z">The Z component of the vector.</param>
/// <param name="w">The W component of the vector.</param>
/// <remarks>
/// <para><b>Memory Layout:</b> 32 bytes, 16-byte aligned (blittable value type).</para>
/// <para><b>Execution Target:</b> Usable on both Host (CPU) and Device (GPU kernels).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct ULong4(ulong x, ulong y, ulong z, ulong w)
{
    /// <summary>The X component of the vector.</summary>
    public ulong X = x;

    /// <summary>The Y component of the vector.</summary>
    public ulong Y = y;

    /// <summary>The Z component of the vector.</summary>
    public ulong Z = z;

    /// <summary>The W component of the vector.</summary>
    public ulong W = w;
}


