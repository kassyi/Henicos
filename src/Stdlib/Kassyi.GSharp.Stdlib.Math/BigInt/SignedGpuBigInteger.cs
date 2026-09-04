using System;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// A lightweight value type wrapping a <see cref="GpuBigInteger"/> with a sign bit,
/// used for tracking alternating series or arbitrary signed integers on the GPU.
/// </summary>
public readonly struct SignedGpuBigInteger : IDisposable
{
    /// <summary>
    /// Gets the underlying absolute value buffer on the GPU.
    /// </summary>
    public GpuBigInteger Value { get; }
    
    /// <summary>
    /// Gets a value indicating whether the integer is strictly negative.
    /// </summary>
    public bool IsNegative { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SignedGpuBigInteger"/> struct.
    /// </summary>
    /// <param name="value">The underlying GPU buffer representing the absolute value.</param>
    /// <param name="isNegative"><see langword="true"/> if the number is negative; otherwise, <see langword="false"/>.</param>
    public SignedGpuBigInteger(GpuBigInteger value, bool isNegative)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        IsNegative = isNegative;
    }

    /// <summary>
    /// Disposes the underlying GPU buffer.
    /// </summary>
    public void Dispose()
    {
        Value.Dispose();
    }
}
