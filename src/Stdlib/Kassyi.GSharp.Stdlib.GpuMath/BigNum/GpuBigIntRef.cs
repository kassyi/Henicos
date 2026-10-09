namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

using Core;
using Kernels;

/// <summary>
/// A <see langword="ref struct"/> representing a pointer/reference to a BigInteger limb array in GPU device or shared memory during G# kernel transpilation.
/// </summary>
/// <param name="data">The underlying 32-bit unsigned integer array buffer.</param>
/// <remarks>
/// <para>Overloads C# arithmetic and bitwise operators to allow intuitive syntax (<c>a = b + c</c>) inside GPU kernel code.</para>
/// <para>During G# compilation, the Roslyn Source Generator intercepts these operator macros and emits optimized device function calls.</para>
/// <para><b>Execution Target:</b> GPU device functions and kernels only. Calling these operators on the host throws <see cref="GpuOnlyException"/>.</para>
/// </remarks>
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public ref struct GpuBigIntRef(uint[] data)
{
    /// <summary>
    /// Gets the raw underlying 32-bit unsigned limb array buffer.
    /// </summary>
    public readonly uint[] Data = data;

    /// <summary>Adds two big integer limb references on the GPU.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>A reference to the resulting big integer buffer.</returns>
    /// <exception cref="GpuOnlyException">Thrown when invoked on the host (CPU).</exception>
    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteAdd))]
    public static GpuBigIntRef operator +(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    /// <summary>Subtracts one big integer limb reference from another on the GPU.</summary>
    /// <param name="a">The minuend operand.</param>
    /// <param name="b">The subtrahend operand.</param>
    /// <returns>A reference to the resulting big integer buffer.</returns>
    /// <exception cref="GpuOnlyException">Thrown when invoked on the host (CPU).</exception>
    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteSub))]
    public static GpuBigIntRef operator -(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    /// <summary>Shifts a big integer limb reference left on the GPU.</summary>
    /// <param name="a">The operand to shift.</param>
    /// <param name="shiftBits">The bit shift amount.</param>
    /// <returns>A reference to the resulting big integer buffer.</returns>
    /// <exception cref="GpuOnlyException">Thrown when invoked on the host (CPU).</exception>
    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteShiftLeft))]
    public static GpuBigIntRef operator <<(GpuBigIntRef a, int shiftBits) => throw new GpuOnlyException();

    /// <summary>Shifts a big integer limb reference right on the GPU.</summary>
    /// <param name="a">The operand to shift.</param>
    /// <param name="shiftBits">The bit shift amount.</param>
    /// <returns>A reference to the resulting big integer buffer.</returns>
    /// <exception cref="GpuOnlyException">Thrown when invoked on the host (CPU).</exception>
    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteShiftRight))]
    public static GpuBigIntRef operator >>(GpuBigIntRef a, int shiftBits) => throw new GpuOnlyException();

    /// <summary>Performs bitwise AND of two big integer limb references on the GPU.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>A reference to the resulting big integer buffer.</returns>
    /// <exception cref="GpuOnlyException">Thrown when invoked on the host (CPU).</exception>
    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteBitwiseAnd))]
    public static GpuBigIntRef operator &(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    /// <summary>Performs bitwise OR of two big integer limb references on the GPU.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>A reference to the resulting big integer buffer.</returns>
    /// <exception cref="GpuOnlyException">Thrown when invoked on the host (CPU).</exception>
    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteBitwiseOr))]
    public static GpuBigIntRef operator |(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    /// <summary>Performs bitwise XOR of two big integer limb references on the GPU.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>A reference to the resulting big integer buffer.</returns>
    /// <exception cref="GpuOnlyException">Thrown when invoked on the host (CPU).</exception>
    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteBitwiseXor))]
    public static GpuBigIntRef operator ^(GpuBigIntRef a, GpuBigIntRef b) => throw new GpuOnlyException();

    /// <summary>Performs bitwise NOT of a big integer limb reference on the GPU.</summary>
    /// <param name="a">The operand to invert.</param>
    /// <returns>A reference to the resulting big integer buffer.</returns>
    /// <exception cref="GpuOnlyException">Thrown when invoked on the host (CPU).</exception>
    [GpuOperatorMacro(typeof(GpuBigIntKernels), nameof(GpuBigIntKernels.ExecuteBitwiseNot))]
    public static GpuBigIntRef operator ~(GpuBigIntRef a) => throw new GpuOnlyException();

    /// <summary>Implicitly converts a raw <see cref="uint"/> array to a <see cref="GpuBigIntRef"/> wrapper.</summary>
    /// <param name="data">The raw limb array.</param>
    /// <returns>A new <see cref="GpuBigIntRef"/> wrapping <paramref name="data"/>.</returns>
    public static implicit operator GpuBigIntRef(uint[] data) => new GpuBigIntRef(data);

    /// <summary>Implicitly converts a <see cref="GpuBigIntRef"/> instance back to its underlying raw <see cref="uint"/> array.</summary>
    /// <param name="data">The <see cref="GpuBigIntRef"/> instance.</param>
    /// <returns>The underlying array buffer.</returns>
    public static implicit operator uint[](GpuBigIntRef data) => data.Data;
}

