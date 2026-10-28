namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A borrowed view of a contiguous range of elements of a <see cref="GpuBigFloatArray"/>: views of the significands,
/// the exponents and the signs.
/// </summary>
/// <remarks>
/// The same rules as <see cref="GpuBigUIntView"/> apply: the view is a <see langword="ref struct"/> that lives only
/// inside the call that obtained it, owns nothing, has no <c>Dispose</c>, and must not be used after its owner is
/// disposed. A <see cref="GpuBigFloatArray"/> converts to a view implicitly.
/// </remarks>
public readonly ref struct GpuBigFloatArrayView
{
    internal GpuBigFloatArrayView(GpuBigUIntArrayView significands, GpuBigUIntArrayView exponents,
        GpuBigUIntArrayView signs)
    {
        if (significands.Length != exponents.Length || significands.Length != signs.Length)
        {
            throw new ArgumentException("Significands, Exponents, and Signs array lengths must match.");
        }

        Significands = significands;
        Exponents = exponents;
        Signs = signs;
    }

    /// <summary>
    /// Implicitly converts a <see cref="GpuBigFloatArray"/> to a view of all its elements.
    /// </summary>
    /// <param name="array">The array to view.</param>
    public static implicit operator GpuBigFloatArrayView(GpuBigFloatArray array) =>
        new(array.Significands, array.Exponents, array.Signs);

    /// <summary>
    /// Gets the view of the significands.
    /// </summary>
    public GpuBigUIntArrayView Significands { get; }

    /// <summary>
    /// Gets the view of the exponents (1 word per element, two's complement <see cref="int"/> bits).
    /// </summary>
    public GpuBigUIntArrayView Exponents { get; }

    /// <summary>
    /// Gets the view of the sign words (1 word per element, 0 = positive, 1 = negative).
    /// </summary>
    public GpuBigUIntArrayView Signs { get; }

    /// <summary>
    /// Gets the number of elements in the view.
    /// </summary>
    public int Length => Significands.Length;

    /// <summary>
    /// Gets the number of 32-bit significand words per element.
    /// </summary>
    public int WordsPerElement => Significands.WordsPerElement;

    /// <summary>
    /// Gets the <see cref="GpuWorkspace"/> of the viewed buffers.
    /// </summary>
    public GpuWorkspace Workspace => Significands.Workspace;

    /// <summary>
    /// Gets a value indicating whether this is the empty view (<see langword="default"/>).
    /// </summary>
    public bool IsEmpty => Significands.IsEmpty;

    /// <summary>
    /// Returns a view of the element at <paramref name="index"/>; reads its exponent and sign words back from the device.
    /// </summary>
    /// <param name="index">The zero-based index of the element.</param>
    /// <returns>A view of the element's significand with its exponent and sign.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the view.</exception>
    public GpuBigFloatView GetElement(int index)
    {
        var significand = Significands.GetElement(index);
        var exponentWord = Exponents.GetElement(index);
        var signWord = Signs.GetElement(index);

        var workspace = significand.Workspace;
        var provider = workspace.Provider;
        var stream = workspace.Stream;
        var exponent = unchecked((int)provider.ReadWord(exponentWord.DevicePointer, stream: stream));
        var isNegative = provider.ReadWord(signWord.DevicePointer, stream: stream) != 0;

        return new GpuBigFloatView(significand, exponent, isNegative);
    }

    /// <summary>
    /// Returns a view of <paramref name="elementCount"/> elements from <paramref name="elementOffset"/>.
    /// </summary>
    /// <param name="elementOffset">The first element, relative to this view.</param>
    /// <param name="elementCount">The number of elements.</param>
    /// <returns>The narrower view.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside this view.</exception>
    public GpuBigFloatArrayView Slice(int elementOffset, int elementCount) =>
        new(Significands.Slice(elementOffset, elementCount), Exponents.Slice(elementOffset, elementCount),
            Signs.Slice(elementOffset, elementCount));

    /// <summary>
    /// Returns the view as a <see cref="GpuBigFloatArray"/> that does not own the buffers, for the internal plumbing;
    /// disposing it frees nothing.
    /// </summary>
    /// <returns>A borrowed <see cref="GpuBigFloatArray"/>.</returns>
    internal GpuBigFloatArray AsBorrowed() => new(Significands.AsBorrowed(), Exponents.AsBorrowed(), Signs.AsBorrowed());
}
