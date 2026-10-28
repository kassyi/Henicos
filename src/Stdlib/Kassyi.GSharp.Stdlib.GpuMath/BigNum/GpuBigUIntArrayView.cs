namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A borrowed view of a contiguous range of elements of a <see cref="GpuBigUIntArray"/>.
/// </summary>
/// <remarks>
/// The same rules as <see cref="GpuBigUIntView"/> apply: the view is a <see langword="ref struct"/> that lives only
/// inside the call that obtained it, owns nothing, has no <c>Dispose</c>, and must not be used after its owner is
/// disposed. A <see cref="GpuBigUIntArray"/> converts to a view implicitly.
/// </remarks>
public readonly ref struct GpuBigUIntArrayView
{
    internal GpuBigUIntArrayView(GpuWorkspace workspace, GpuBigIntMemoryHandle handle, int length, int wordsPerElement)
    {
        Workspace = workspace;
        Handle = handle.Borrow();
        Length = length;
        WordsPerElement = wordsPerElement;
    }

    /// <summary>
    /// Implicitly converts a <see cref="GpuBigUIntArray"/> to a view of all its elements.
    /// </summary>
    /// <param name="array">The array to view.</param>
    public static implicit operator GpuBigUIntArrayView(GpuBigUIntArray array) =>
        new(array.Workspace, array.Handle, array.Length, array.WordsPerElement);

    /// <summary>
    /// Gets the borrowed handle of the viewed words (never owning).
    /// </summary>
    internal GpuBigIntMemoryHandle Handle { get; }

    /// <summary>
    /// Gets the <see cref="GpuWorkspace"/> of the viewed buffer, or <see langword="null"/> for an empty view.
    /// </summary>
    public GpuWorkspace Workspace { get; }

    /// <summary>
    /// Gets the number of elements in the view.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the number of 32-bit words per element.
    /// </summary>
    public int WordsPerElement { get; }

    /// <summary>
    /// Gets the total number of 32-bit words in the view (<c>Length * WordsPerElement</c>).
    /// </summary>
    public int TotalWords => Length * WordsPerElement;

    /// <summary>
    /// Gets the raw 64-bit device memory pointer to the first element of the view.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The owner of the buffer has been disposed.</exception>
    public IntPtr DevicePointer => Handle.DevicePointer;

    /// <summary>
    /// Gets a value indicating whether this is the empty view (<see langword="default"/>).
    /// </summary>
    public bool IsEmpty => Handle.IsDefault;

    /// <summary>
    /// Returns a view of the element at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The zero-based index of the element.</param>
    /// <returns>A view of the element's words.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the view.</exception>
    public GpuBigUIntView GetElement(int index)
    {
        if (index < 0 || index >= Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new GpuBigUIntView(Workspace, Handle.Slice(index * WordsPerElement, WordsPerElement));
    }

    /// <summary>
    /// Returns a view of <paramref name="elementCount"/> elements from <paramref name="elementOffset"/>.
    /// </summary>
    /// <param name="elementOffset">The first element, relative to this view.</param>
    /// <param name="elementCount">The number of elements.</param>
    /// <returns>The narrower view.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside this view.</exception>
    public GpuBigUIntArrayView Slice(int elementOffset, int elementCount)
    {
        if (elementOffset < 0 || elementCount < 0 || elementOffset > Length - elementCount)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        return new GpuBigUIntArrayView(Workspace,
            Handle.Slice(elementOffset * WordsPerElement, elementCount * WordsPerElement), elementCount, WordsPerElement);
    }

    /// <summary>
    /// Returns the view as a <see cref="GpuBigUIntArray"/> that does not own the buffer, for the internal plumbing;
    /// disposing it frees nothing.
    /// </summary>
    /// <returns>A borrowed <see cref="GpuBigUIntArray"/>.</returns>
    internal GpuBigUIntArray AsBorrowed() => new(Workspace, Handle, Length, WordsPerElement);

    /// <summary>
    /// Returns the length and the handle of the view; it does not read device memory.
    /// </summary>
    /// <returns>A string for debugging.</returns>
    public override string ToString() =>
        $"GpuBigUIntArrayView(Length={Length}, WordsPerElement={WordsPerElement}, {Handle})";
}
