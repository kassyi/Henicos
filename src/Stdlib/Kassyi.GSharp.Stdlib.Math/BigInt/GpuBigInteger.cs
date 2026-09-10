namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// A lightweight value type wrapping a <see cref="GpuUIntBigInteger"/> with a sign bit,
/// used for tracking alternating series or arbitrary signed integers on the GPU.
/// </summary>
public readonly struct GpuBigInteger(GpuUIntBigInteger value, bool isNegative) : IDisposable
{
    /// <summary>
    /// Gets the underlying absolute value buffer on the GPU.
    /// </summary>
    public GpuUIntBigInteger Value { get; } = value ?? throw new ArgumentNullException(nameof(value));
    
    /// <summary>
    /// Gets a value indicating whether the integer is strictly negative.
    /// </summary>
    public bool IsNegative { get; } = isNegative;

    /// <summary>
    /// Disposes the underlying GPU buffer.
    /// </summary>
    public void Dispose()
    {
        Value.Dispose();
    }
}
