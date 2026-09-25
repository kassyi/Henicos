namespace Kassyi.GSharp.Stdlib.Math.BigInt;

using Core;
using Kernels;

/// <summary>
/// A ref struct representing a pointer to a BigInteger limb array in GPU device/shared memory during G# kernel transpilation.
/// </summary>
/// <remarks>
/// <para>Overloads C# arithmetic operators to permit expressions like <c>a = b + c</c> inside GPU kernels. The GSharp Transpiler replaces these with optimized device function calls.</para>
/// <para><b>Target:</b> GPU device functions and kernels only.</para>
/// </remarks>
public readonly ref struct GpuBigIntRef(uint[] data)
{
    /// <summary>Gets the raw underlying uint limb array buffer.</summary>
    public readonly uint[] Data = data;

    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteAdd))]
    public static GpuBigIntRef operator +(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteSub))]
    public static GpuBigIntRef operator -(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteShiftLeft))]
    public static GpuBigIntRef operator <<(GpuBigIntRef a, int shiftBits) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteShiftRight))]
    public static GpuBigIntRef operator >>(GpuBigIntRef a, int shiftBits) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteBitwiseAnd))]
    public static GpuBigIntRef operator &(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteBitwiseOr))]
    public static GpuBigIntRef operator |(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteBitwiseXor))]
    public static GpuBigIntRef operator ^(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteBitwiseNot))]
    public static GpuBigIntRef operator ~(GpuBigIntRef a) => throw new GpuOnlyException();

    // Implicit cast from uint[] so we can easily pass ctx.Buffers.X directly.
    public static implicit operator GpuBigIntRef(uint[] data) => new GpuBigIntRef(data);
    public static implicit operator uint[](GpuBigIntRef data) => data.Data;
}

