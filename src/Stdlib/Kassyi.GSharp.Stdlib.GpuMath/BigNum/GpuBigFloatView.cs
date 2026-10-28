namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A borrowed view of a signed arbitrary-precision floating-point number: a significand view, an exponent and a sign.
/// </summary>
/// <remarks>
/// The same rules as <see cref="GpuBigUIntView"/> apply: the view is a <see langword="ref struct"/> that lives only
/// inside the call that obtained it, owns nothing, has no <c>Dispose</c>, and must not be used after its owner is
/// disposed. A <see cref="GpuBigFloat"/> converts to a view implicitly; use <see cref="ToOwned"/> to keep the value.
/// The value is <c>(-1)^IsNegative * Significand * 2^Exponent</c>.
/// </remarks>
public readonly ref struct GpuBigFloatView
{
    internal GpuBigFloatView(GpuBigUIntView significand, int exponent, bool isNegative)
    {
        Significand = significand;
        Exponent = exponent;
        IsNegative = isNegative;
    }

    /// <summary>
    /// Implicitly converts a <see cref="GpuBigFloat"/> to a view of its significand, exponent and sign.
    /// </summary>
    /// <param name="value">The value to view.</param>
    public static implicit operator GpuBigFloatView(GpuBigFloat value) =>
        new(value.UFloat.Significand, value.UFloat.Exponent, value.IsNegative);

    /// <summary>
    /// Gets the view of the significand.
    /// </summary>
    public GpuBigUIntView Significand { get; }

    /// <summary>
    /// Gets the binary exponent.
    /// </summary>
    public int Exponent { get; }

    /// <summary>
    /// Gets a value indicating whether the value is negative.
    /// </summary>
    public bool IsNegative { get; }

    /// <summary>
    /// Gets the <see cref="GpuWorkspace"/> of the viewed buffer.
    /// </summary>
    public GpuWorkspace Workspace => Significand.Workspace;

    /// <summary>
    /// Gets a value indicating whether this is the empty view (<see langword="default"/>).
    /// </summary>
    public bool IsEmpty => Significand.IsEmpty;

    /// <summary>
    /// Returns the view as a <see cref="GpuBigFloat"/> that does not own the significand buffer, for the internal
    /// plumbing; disposing it frees nothing.
    /// </summary>
    /// <returns>A borrowed <see cref="GpuBigFloat"/>.</returns>
    internal GpuBigFloat AsBorrowed() => new(Significand.AsBorrowedBigUInt(), Exponent, IsNegative);

    /// <summary>
    /// Copies the value into a new buffer that the caller owns.
    /// </summary>
    /// <returns>
    /// A new <see cref="GpuBigFloat"/> with the same value; dispose it. The empty view returns one whose significand is
    /// <see langword="default"/>.
    /// </returns>
    public GpuBigFloat ToOwned() => new(Significand.ToOwned(), Exponent, IsNegative);

    /// <inheritdoc cref="GpuBigFloat.IsZero"/>
    public bool IsZero() => Significand.IsZero();

    /// <summary>
    /// Compares the value of this view with the value of another view on the GPU.
    /// </summary>
    /// <param name="other">The view to compare with.</param>
    /// <returns>Less than zero, zero or greater than zero as this value is less than, equal to or greater than <paramref name="other"/>.</returns>
    public int CompareTo(scoped GpuBigFloatView other) => AsBorrowed().CompareTo(other.AsBorrowed());

    /// <summary>
    /// Returns the exponent, the sign and the significand view; it does not read device memory.
    /// </summary>
    /// <returns>A string for debugging.</returns>
    public override string ToString() =>
        $"GpuBigFloatView(Exponent={Exponent}, IsNegative={IsNegative}, {Significand.ToString()})";
}
