namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents a contiguous array of arbitrary-precision unsigned integers stored in NVIDIA GPU global device memory.
/// Designed for high-performance Batched / Vectorized execution and parallel reductions.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct GpuBigUIntArray : IDisposable, IEquatable<GpuBigUIntArray>
{
    /// <summary>
    /// Gets the <see cref="GpuWorkspace"/> associated with this GPU array instance.
    /// </summary>
    public GpuWorkspace Workspace { get; }

    /// <summary>
    /// Gets the underlying GPU memory handle tracking device buffer allocation and lifetime.
    /// </summary>
    public GpuBigIntMemoryHandle Handle { get; }

    /// <summary>
    /// Gets the raw 64-bit device memory pointer to the first element in GPU global address space.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The buffer has been freed (this array or a copy of it was disposed).</exception>
    public IntPtr DevicePointer => Handle.DevicePointer;

    /// <summary>
    /// Gets the number of elements in the array.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets the number of 32-bit unsigned integer limbs (words) per element.
    /// </summary>
    public int WordsPerElement { get; }

    /// <summary>
    /// Gets the total number of 32-bit words allocated for this array (Length * WordsPerElement).
    /// </summary>
    public int TotalWords => Length * WordsPerElement;

    /// <summary>
    /// Gets a value indicating whether this array owns its device buffer: disposing it returns the buffer to the pool.
    /// </summary>
    public bool OwnsBuffer => Handle.OwnsBuffer;

    internal GpuBigUIntArray(GpuWorkspace workspace, GpuBigIntMemoryHandle handle, int length, int wordsPerElement)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Handle = handle;
        Length = length;
        WordsPerElement = wordsPerElement;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigUIntArray"/> struct allocating zero-filled GPU device memory.
    /// </summary>
    /// <param name="workspace">The GPU workspace managing memory allocations.</param>
    /// <param name="length">The number of elements in the array.</param>
    /// <param name="wordsPerElement">The number of 32-bit words per element.</param>
    public GpuBigUIntArray(GpuWorkspace workspace, int length, int wordsPerElement)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Length = length;
        WordsPerElement = wordsPerElement;
        Handle = Workspace.MemoryManager.Allocate(length * wordsPerElement);
    }

    /// <summary>
    /// Gets a view of the specified element without allocating a new device buffer.
    /// </summary>
    /// <param name="index">The index of the element.</param>
    /// <returns>
    /// A borrowed view of the element's device memory. It lives only inside the calling method (a
    /// <see langword="ref struct"/>) and must not be used after this array is disposed; copy it with
    /// <see cref="GpuBigUIntView.ToOwned"/> to keep the value.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the array.</exception>
    public GpuBigUIntView GetElement(int index) => ((GpuBigUIntArrayView)this).GetElement(index);

    /// <summary>
    /// Returns the array argument that passes the words of every element to a <c>Launch{Kernel}Ptr</c> launcher or a
    /// shadow struct field: the kernel sees an array of <see cref="TotalWords"/> words.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The buffer has been freed.</exception>
    public GSharpArrayProxy ToProxy() => new() { Data = DevicePointer, Length = TotalWords };

    /// <summary>
    /// Returns the element at <paramref name="index"/> as a <see cref="GpuBigUInt"/> that does not own the buffer, for
    /// the internal plumbing that needs the owned type; disposing it frees nothing.
    /// </summary>
    /// <param name="index">The index of the element.</param>
    /// <returns>A borrowed <see cref="GpuBigUInt"/>.</returns>
    internal GpuBigUInt BorrowElement(int index) => GetElement(index).AsBorrowedBigUInt();

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
    public GpuBigUIntArrayView Slice(int elementOffset, int elementCount) =>
        ((GpuBigUIntArrayView)this).Slice(elementOffset, elementCount);

    /// <summary>
    /// Returns a range of elements as a <see cref="GpuBigUIntArray"/> that does not own the buffer, for the internal
    /// plumbing that needs the owned type (for example a vector expression that returns its input); disposing it frees
    /// nothing.
    /// </summary>
    /// <param name="elementOffset">The starting element index.</param>
    /// <param name="elementCount">The number of elements.</param>
    /// <returns>A borrowed <see cref="GpuBigUIntArray"/>.</returns>
    internal GpuBigUIntArray BorrowSlice(int elementOffset, int elementCount) =>
        Slice(elementOffset, elementCount).AsBorrowed();

    /// <summary>
    /// Returns a view of the same elements that does not own the buffer; disposing the view does nothing.
    /// </summary>
    /// <returns>A borrowed <see cref="GpuBigUIntArray"/>, valid while this array is not disposed.</returns>
    internal GpuBigUIntArray AsBorrowed() => new(Workspace, Handle.Borrow(), Length, WordsPerElement);

    /// <summary>
    /// Indicates whether the current instance refers to the same workspace, device buffer slice, and element shape as
    /// another <see cref="GpuBigUIntArray"/>.
    /// </summary>
    /// <param name="other">A <see cref="GpuBigUIntArray"/> to compare with this instance.</param>
    /// <returns>
    /// <see langword="true"/> if both instances share the same <see cref="Workspace"/>, <see cref="Handle"/>,
    /// <see cref="Length"/>, and <see cref="WordsPerElement"/>; otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <see cref="Equals(GpuBigUIntArray)"/> and <see cref="GetHashCode"/> compare buffer identity and array shape
    /// rather than element values: two separately allocated arrays holding identical values are not equal, whereas a
    /// full-range borrowed view shares the same handle identity. Neither member reads device memory, so both also work
    /// on <see langword="default"/> or disposed instances.
    /// </remarks>
    public bool Equals(GpuBigUIntArray other) =>
        ReferenceEquals(Workspace, other.Workspace) &&
        Handle.Equals(other.Handle) &&
        Length == other.Length &&
        WordsPerElement == other.WordsPerElement;

    /// <summary>
    /// Determines whether the specified object is equal to the current instance.
    /// </summary>
    /// <param name="obj">The object to compare with the current instance.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="obj"/> is a <see cref="GpuBigUIntArray"/> and equals the current
    /// instance; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is GpuBigUIntArray other && Equals(other);

    /// <summary>
    /// Returns a hash code based on the workspace, buffer handle identity, and array shape, consistent with
    /// <see cref="Equals(GpuBigUIntArray)"/>.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(RuntimeHelpers.GetHashCode(Workspace), Handle, Length, WordsPerElement);

    /// <summary>
    /// Releases the GPU global device memory resources allocated for the underlying buffer.
    /// </summary>
    /// <remarks>
    /// A borrowed array (a slice, or another view that does not own its buffer) frees nothing.
    /// </remarks>
    public void Dispose()
    {
        if (Handle.OwnsBuffer)
        {
            Workspace.MemoryManager.Free(Handle);
        }
    }
}
