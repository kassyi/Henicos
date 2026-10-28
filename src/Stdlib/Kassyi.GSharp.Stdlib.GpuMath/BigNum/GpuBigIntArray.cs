namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents a contiguous array of arbitrary-precision signed integers stored in NVIDIA GPU global device memory.
/// </summary>
public readonly struct GpuBigIntArray : IDisposable
{
    /// <summary>
    /// Gets the underlying contiguous array of absolute values.
    /// </summary>
    public GpuBigUIntArray Values { get; }

    /// <summary>
    /// Gets the underlying contiguous array of sign indicators (1 word per element, 0 = positive, 1 = negative).
    /// </summary>
    public GpuBigUIntArray Signs { get; }

    /// <summary>
    /// Gets the length of the array (number of elements).
    /// </summary>
    public int Length => Values.Length;

    /// <summary>
    /// Gets the number of 32-bit words per element.
    /// </summary>
    public int WordsPerElement => Values.WordsPerElement;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigIntArray"/> struct allocating uninitialized GPU device memory.
    /// </summary>
    /// <remarks>
    /// Neither <see cref="Values"/> nor <see cref="Signs"/> is cleared. Vector operations, including the comparisons of
    /// <see cref="Expressions.GpuBigIntVectorLinqExtensions"/>, read the sign word of each element, so write
    /// <see cref="Signs"/> together with <see cref="Values"/> when filling the array.
    /// </remarks>
    /// <param name="workspace">The GPU workspace managing memory allocations.</param>
    /// <param name="length">The number of elements in the array.</param>
    /// <param name="wordsPerElement">The number of 32-bit words per element.</param>
    public GpuBigIntArray(GpuWorkspace workspace, int length, int wordsPerElement)
    {
        Values = new GpuBigUIntArray(workspace, length, wordsPerElement);
        try
        {
            Signs = new GpuBigUIntArray(workspace, length, 1);
        }
        catch
        {
            Values.Dispose();
            throw;
        }
    }

    internal GpuBigIntArray([TakesOwnership] GpuBigUIntArray values, [TakesOwnership] GpuBigUIntArray signs)
    {
        if (values.Length != signs.Length)
        {
            throw new ArgumentException("Values and Signs array lengths must match.");
        }

        Values = values;
        Signs = signs;
    }

    /// <summary>
    /// Gets a view of the specified element (for debugging, involves slow device-to-host read of the sign).
    /// </summary>
    /// <param name="index">The index of the element.</param>
    /// <returns>
    /// A borrowed view of the element's device memory and its sign. It lives only inside the calling method (a
    /// <see langword="ref struct"/>) and must not be used after this array is disposed.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the array.</exception>
    public GpuBigIntView GetElement(int index) => ((GpuBigIntArrayView)this).GetElement(index);

    /// <summary>
    /// Returns the element at <paramref name="index"/> as a <see cref="GpuBigInt"/> that does not own the buffer, for the
    /// internal plumbing that needs the owned type; disposing it frees nothing.
    /// </summary>
    /// <param name="index">The index of the element.</param>
    /// <returns>A borrowed <see cref="GpuBigInt"/>.</returns>
    internal GpuBigInt BorrowElement(int index) => GetElement(index).AsBorrowed();

    /// <summary>
    /// Creates a zero-allocation sub-slice of this GPU array.
    /// </summary>
    /// <param name="elementOffset">The starting element index of the slice.</param>
    /// <param name="elementCount">The number of elements in the slice.</param>
    /// <returns>
    /// A borrowed view of the elements. It lives only inside the calling method (a <see langword="ref struct"/>) and must
    /// not be used after this array is disposed.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside the array.</exception>
    public GpuBigIntArrayView Slice(int elementOffset, int elementCount) =>
        ((GpuBigIntArrayView)this).Slice(elementOffset, elementCount);

    /// <summary>
    /// Returns a range of elements as a <see cref="GpuBigIntArray"/> that does not own the buffers, for the internal
    /// plumbing that needs the owned type; disposing it frees nothing.
    /// </summary>
    /// <param name="elementOffset">The starting element index.</param>
    /// <param name="elementCount">The number of elements.</param>
    /// <returns>A borrowed <see cref="GpuBigIntArray"/>.</returns>
    internal GpuBigIntArray BorrowSlice(int elementOffset, int elementCount) =>
        Slice(elementOffset, elementCount).AsBorrowed();

    /// <summary>
    /// Releases the GPU global device memory resources allocated for the underlying buffers.
    /// </summary>
    public void Dispose()
    {
        Values.Dispose();
        Signs.Dispose();
    }
}
