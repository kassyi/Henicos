namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A borrowed view of a signed arbitrary-precision integer in GPU device memory: a magnitude view and a sign.
/// </summary>
/// <remarks>
/// The same rules as <see cref="GpuBigUIntView"/> apply: the view is a <see langword="ref struct"/> that lives only
/// inside the call that obtained it, owns nothing, has no <c>Dispose</c>, and must not be used after its owner is
/// disposed. A <see cref="GpuBigInt"/> converts to a view implicitly; use <see cref="ToOwned"/> to keep the value.
/// </remarks>
public readonly ref struct GpuBigIntView
{
    internal GpuBigIntView(GpuBigUIntView value, bool isNegative)
    {
        Value = value;
        IsNegative = isNegative;
    }

    /// <summary>
    /// Implicitly converts a <see cref="GpuBigInt"/> to a view of its magnitude and sign.
    /// </summary>
    /// <param name="value">The value to view.</param>
    public static implicit operator GpuBigIntView(GpuBigInt value) => new(value.Value, value.IsNegative);

    /// <summary>
    /// Gets the view of the absolute value.
    /// </summary>
    public GpuBigUIntView Value { get; }

    /// <summary>
    /// Gets a value indicating whether the value is negative.
    /// </summary>
    public bool IsNegative { get; }

    /// <summary>
    /// Gets the <see cref="GpuWorkspace"/> of the viewed buffer.
    /// </summary>
    public GpuWorkspace Workspace => Value.Workspace;

    /// <summary>
    /// Gets a value indicating whether this is the empty view (<see langword="default"/>).
    /// </summary>
    public bool IsEmpty => Value.IsEmpty;

    /// <summary>
    /// Returns the view as a <see cref="GpuBigInt"/> that does not own the buffer, for the internal plumbing; disposing it
    /// frees nothing.
    /// </summary>
    /// <returns>A borrowed <see cref="GpuBigInt"/>.</returns>
    internal GpuBigInt AsBorrowed() => new(Value.AsBorrowedBigUInt(), IsNegative);

    /// <summary>
    /// Copies the value into a new buffer that the caller owns.
    /// </summary>
    /// <returns>
    /// A new <see cref="GpuBigInt"/> with the same value; dispose it. The empty view returns a <see cref="GpuBigInt"/>
    /// whose <see cref="GpuBigInt.Value"/> is <see langword="default"/>.
    /// </returns>
    public GpuBigInt ToOwned() => new(Value.ToOwned(), IsNegative);

    /// <inheritdoc cref="GpuBigInt.IsZero"/>
    public bool IsZero() => Value.IsZero();

    /// <inheritdoc cref="GpuBigInt.GetBitLength"/>
    public int GetBitLength() => Value.GetBitLength();

    /// <summary>
    /// Compares the value of this view with the value of another view on the GPU; <c>+0</c> equals <c>-0</c>.
    /// </summary>
    /// <param name="other">The view to compare with.</param>
    /// <returns>Less than zero, zero or greater than zero as this value is less than, equal to or greater than <paramref name="other"/>.</returns>
    public int CompareTo(scoped GpuBigIntView other) => AsBorrowed().CompareTo(other.AsBorrowed());

    /// <summary>
    /// Reads the value back to the host.
    /// </summary>
    /// <returns>The value as a <see cref="BigInteger"/>.</returns>
    public BigInteger ToBigInteger() => Value.ToBigInteger(IsNegative);

    /// <summary>
    /// Returns the sign and the magnitude view; it does not read device memory.
    /// </summary>
    /// <returns>A string for debugging.</returns>
    public override string ToString() => $"GpuBigIntView(IsNegative={IsNegative}, {Value.ToString()})";
}
