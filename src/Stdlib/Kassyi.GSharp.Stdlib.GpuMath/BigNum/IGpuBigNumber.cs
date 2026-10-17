namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents an arbitrary-precision GPU big number backed by a <see cref="GpuBigUInt"/> magnitude buffer in device memory,
/// unifying <see cref="GpuBigUInt"/>, <see cref="GpuBigInt"/>, <see cref="GpuBigUFloat"/>, and <see cref="GpuBigFloat"/> under a single type parameter.
/// </summary>
/// <typeparam name="TSelf">The concrete struct type implementing this interface.</typeparam>
public interface IGpuBigNumber<TSelf> : IGpuNumber<TSelf>, IDisposable
    where TSelf : struct, IGpuBigNumber<TSelf>
{
    /// <summary>
    /// Gets the <see cref="GpuWorkspace"/> managing device memory and stream execution for this number.
    /// </summary>
    GpuWorkspace Workspace { get; }

    /// <summary>
    /// Gets the unsigned magnitude (mantissa/significand) buffer stored in GPU device memory.
    /// </summary>
    GpuBigUInt Magnitude { get; }

    /// <summary>
    /// Gets the binary base-2 exponent ($E$ in $\text{Magnitude} \times 2^E$). Always 0 for integer types.
    /// </summary>
    int Exponent { get; }

    /// <summary>
    /// Gets the effective binary scale ($\text{Exponent} + \text{GetBitLength}()$) of a non-zero number.
    /// </summary>
    long EffectiveScale { get; }

    /// <summary>
    /// Creates a new instance of <typeparamref name="TSelf"/> from its unified mathematical components.
    /// </summary>
    /// <param name="magnitude">The unsigned magnitude buffer.</param>
    /// <param name="exponent">The base-2 exponent (must be 0 for integer types).</param>
    /// <param name="isNegative">The sign flag (must be <see langword="false"/> for unsigned types unless magnitude is zero).</param>
    /// <returns>A new <typeparamref name="TSelf"/> instance.</returns>
    static abstract TSelf Create(GpuBigUInt magnitude, int exponent, bool isNegative);

    /// <summary>
    /// Adds two <typeparamref name="TSelf"/> instances and returns the evaluated result in GPU memory.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>A newly allocated <typeparamref name="TSelf"/> representing the sum.</returns>
    static abstract TSelf operator +(TSelf left, TSelf right);

    /// <summary>
    /// Subtracts one <typeparamref name="TSelf"/> instance from another and returns the evaluated result in GPU memory.
    /// </summary>
    /// <param name="left">The minuend.</param>
    /// <param name="right">The subtrahend.</param>
    /// <returns>A newly allocated <typeparamref name="TSelf"/> representing the difference.</returns>
    static abstract TSelf operator -(TSelf left, TSelf right);

    /// <summary>
    /// Multiplies two <typeparamref name="TSelf"/> instances and returns the evaluated result in GPU memory.
    /// </summary>
    /// <param name="left">The left factor.</param>
    /// <param name="right">The right factor.</param>
    /// <returns>A newly allocated <typeparamref name="TSelf"/> representing the product.</returns>
    static abstract TSelf operator *(TSelf left, TSelf right);

    /// <summary>
    /// Divides one <typeparamref name="TSelf"/> instance by another and returns the evaluated result in GPU memory.
    /// </summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>A newly allocated <typeparamref name="TSelf"/> representing the quotient.</returns>
    static abstract TSelf operator /(TSelf left, TSelf right);
}
