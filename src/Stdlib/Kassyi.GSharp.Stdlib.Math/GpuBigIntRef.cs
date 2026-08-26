namespace Kassyi.GSharp.Stdlib.Math;

/// <summary>
/// A macro dummy struct for G# Transpiler.
/// Represents a pointer to a BigInt operand array in GPU memory.
/// Overloads operators to allow C# syntax like `a = b + c`, which the G# Transpiler intercepts 
/// and lowers into `BigIntKernelContext` boilerplate and `Device_Add` calls.
/// </summary>
public readonly ref struct GpuBigIntRef
{
    public readonly uint[] Data;

    public GpuBigIntRef(uint[] data)
    {
        Data = data;
    }

    public static GpuBigIntRef operator +(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    public static GpuBigIntRef operator -(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    public static GpuBigIntRef operator <<(GpuBigIntRef a, int shiftBits) => throw new GpuOnlyException();

    public static GpuBigIntRef operator >> (GpuBigIntRef a, int shiftBits) => throw new GpuOnlyException();

    public static GpuBigIntRef operator &(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    public static GpuBigIntRef operator |(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    public static GpuBigIntRef operator ^(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    public static GpuBigIntRef operator ~(GpuBigIntRef a) => throw new GpuOnlyException();

    // Implicit cast from uint[] so we can easily pass ctx.Buffers.X directly.
    public static implicit operator GpuBigIntRef(uint[] data) => new GpuBigIntRef(data);
    public static implicit operator uint[](GpuBigIntRef data) => data.Data;
}
