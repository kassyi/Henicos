namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents a contiguous array of arbitrary-precision unsigned integers stored in NVIDIA GPU global device memory.
/// Designed for high-performance Batched / Vectorized execution and parallel reductions.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct GpuBigUIntArray : IDisposable
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
    /// Gets a value indicating whether this buffer represents an intermediate temporary buffer.
    /// </summary>
    public bool IsTemp { get; }

    internal GpuBigUIntArray(GpuWorkspace workspace, GpuBigIntMemoryHandle handle, int length, int wordsPerElement, bool isTemp = false)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Handle = handle;
        Length = length;
        WordsPerElement = wordsPerElement;
        IsTemp = isTemp;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigUIntArray"/> struct allocating uninitialized GPU device memory.
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
        IsTemp = false;
    }

    /// <summary>
    /// Gets a slice representation of the specified element without allocating a new device buffer.
    /// </summary>
    /// <param name="index">The index of the element.</param>
    /// <returns>
    /// A borrowed <see cref="GpuBigUInt"/> pointing to the element's device memory; it does not own the buffer, so
    /// disposing it frees nothing. Do not use it after this array is disposed.
    /// </returns>
    public GpuBigUInt GetElement(int index)
    {
        if (index < 0 || index >= Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var elementHandle = Handle.Slice(index * WordsPerElement, WordsPerElement);
        return new GpuBigUInt(Workspace, elementHandle, isTemp: false);
    }

    /// <summary>
    /// Creates a zero-allocation sub-slice of this GPU array.
    /// </summary>
    /// <param name="elementOffset">The starting element index of the slice.</param>
    /// <param name="elementCount">The number of elements in the slice.</param>
    /// <returns>
    /// A borrowed view of the elements; it does not own the buffer, so disposing it frees nothing. Do not use it after this
    /// array is disposed.
    /// </returns>
    public GpuBigUIntArray Slice(int elementOffset, int elementCount)
    {
        if (elementOffset < 0 || elementCount < 0 || elementOffset + elementCount > Length)
        {
            throw new ArgumentOutOfRangeException();
        }

        var newHandle = Handle.Slice(elementOffset * WordsPerElement, elementCount * WordsPerElement);
        return new GpuBigUIntArray(Workspace, newHandle, elementCount, WordsPerElement);
    }

    /// <summary>
    /// Returns a view of the same elements that does not own the buffer; disposing the view does nothing.
    /// </summary>
    /// <returns>A borrowed <see cref="GpuBigUIntArray"/>, valid while this array is not disposed.</returns>
    internal GpuBigUIntArray AsBorrowed() => new(Workspace, Handle.Borrow(), Length, WordsPerElement, IsTemp);

    /// <summary>
    /// Releases the GPU global device memory resources allocated for the underlying buffer.
    /// </summary>
    /// <remarks>
    /// A borrowed array (a slice, or another view that does not own its buffer) frees nothing.
    /// </remarks>
    public void Dispose()
    {
        if (!Handle.OwnsBuffer)
        {
            return;
        }

        if (IsTemp)
        {
            // Allocate a dummy GpuBigUInt to use the ReturnTemp method, since ReturnTemp takes GpuBigUInt.
            Workspace.ReturnTemp(new GpuBigUInt(Workspace, Handle, true));
        }
        else
        {
            Workspace.MemoryManager.Free(Handle);
        }
    }
}
