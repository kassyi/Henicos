namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A borrowed view of a contiguous range of elements of a <see cref="GpuBigIntArray"/>: views of the values and of the
/// signs.
/// </summary>
/// <remarks>
/// The same rules as <see cref="GpuBigUIntView"/> apply: the view is a <see langword="ref struct"/> that lives only
/// inside the call that obtained it, owns nothing, has no <c>Dispose</c>, and must not be used after its owner is
/// disposed. A <see cref="GpuBigIntArray"/> converts to a view implicitly.
/// </remarks>
public readonly ref struct GpuBigIntArrayView
{
    internal GpuBigIntArrayView(GpuBigUIntArrayView values, GpuBigUIntArrayView signs)
    {
        if (values.Length != signs.Length)
        {
            throw new ArgumentException("Values and Signs array lengths must match.");
        }

        Values = values;
        Signs = signs;
    }

    /// <summary>
    /// Implicitly converts a <see cref="GpuBigIntArray"/> to a view of all its elements.
    /// </summary>
    /// <param name="array">The array to view.</param>
    public static implicit operator GpuBigIntArrayView(GpuBigIntArray array) => new(array.Values, array.Signs);

    /// <summary>
    /// Gets the view of the absolute values.
    /// </summary>
    public GpuBigUIntArrayView Values { get; }

    /// <summary>
    /// Gets the view of the sign words (1 word per element, 0 = positive, 1 = negative).
    /// </summary>
    public GpuBigUIntArrayView Signs { get; }

    /// <summary>
    /// Gets the number of elements in the view.
    /// </summary>
    public int Length => Values.Length;

    /// <summary>
    /// Gets the number of 32-bit words per element.
    /// </summary>
    public int WordsPerElement => Values.WordsPerElement;

    /// <summary>
    /// Gets the <see cref="GpuWorkspace"/> of the viewed buffers.
    /// </summary>
    public GpuWorkspace Workspace => Values.Workspace;

    /// <summary>
    /// Gets a value indicating whether this is the empty view (<see langword="default"/>).
    /// </summary>
    public bool IsEmpty => Values.IsEmpty;

    /// <summary>
    /// Returns a view of the element at <paramref name="index"/>; reads its sign word back from the device.
    /// </summary>
    /// <param name="index">The zero-based index of the element.</param>
    /// <returns>A view of the element's magnitude with its sign.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the view.</exception>
    public GpuBigIntView GetElement(int index)
    {
        var value = Values.GetElement(index);
        var signWord = Signs.GetElement(index);
        var workspace = signWord.Workspace;
        var isNegative = workspace.Provider.ReadWord(signWord.DevicePointer, stream: workspace.Stream) != 0;
        return new GpuBigIntView(value, isNegative);
    }

    /// <summary>
    /// Returns a view of <paramref name="elementCount"/> elements from <paramref name="elementOffset"/>.
    /// </summary>
    /// <param name="elementOffset">The first element, relative to this view.</param>
    /// <param name="elementCount">The number of elements.</param>
    /// <returns>The narrower view.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside this view.</exception>
    public GpuBigIntArrayView Slice(int elementOffset, int elementCount) =>
        new(Values.Slice(elementOffset, elementCount), Signs.Slice(elementOffset, elementCount));

    /// <summary>
    /// Returns the view as a <see cref="GpuBigIntArray"/> that does not own the buffers, for the internal plumbing;
    /// disposing it frees nothing.
    /// </summary>
    /// <returns>A borrowed <see cref="GpuBigIntArray"/>.</returns>
    internal GpuBigIntArray AsBorrowed() => new(Values.AsBorrowed(), Signs.AsBorrowed());
}
