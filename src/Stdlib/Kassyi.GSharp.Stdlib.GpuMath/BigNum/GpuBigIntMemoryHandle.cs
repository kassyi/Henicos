namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents a structured handle to a slice of device memory storing large integer limbs.
/// </summary>
/// <remarks>
/// <para>
/// This struct pairs a reference to the backing device buffer (<see cref="GpuBigIntDeviceBuffer"/>) with a word offset
/// and limb count, enabling zero-copy sub-allocation and slicing across GPU execution kernels.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> As a readonly record struct, instances are passed by value with no managed heap allocations.
/// </para>
/// </remarks>
public readonly record struct GpuBigIntMemoryHandle
{
    /// <summary>
    /// Gets the backing device memory buffer.
    /// </summary>
    internal GpuBigIntDeviceBuffer Buffer
    {
        get;
    }

    /// <summary>
    /// Gets the 32-bit word (limb) offset within the underlying device buffer.
    /// </summary>
    public int WordOffset
    {
        get;
    }

    /// <summary>
    /// Gets the number of 32-bit words (limbs) allocated to this memory slice.
    /// </summary>
    public int NumWords
    {
        get;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigIntMemoryHandle"/> struct.
    /// </summary>
    /// <param name="buffer">The underlying device buffer containing the limbs.</param>
    /// <param name="wordOffset">The starting word offset in the buffer.</param>
    /// <param name="numWords">The number of words in this slice.</param>
    internal GpuBigIntMemoryHandle(GpuBigIntDeviceBuffer buffer, int wordOffset, int numWords)
    {
        Buffer = buffer;
        WordOffset = wordOffset;
        NumWords = numWords;
    }

    /// <summary>
    /// Gets a value indicating whether this handle represents an uninitialized or default instance.
    /// </summary>
    public bool IsDefault => Buffer == null;

    /// <summary>
    /// Gets the raw 64-bit device memory pointer to the first limb word of this slice.
    /// </summary>
    internal IntPtr DevicePointer => IsDefault ? IntPtr.Zero : IntPtr.Add(Buffer.Pointer, WordOffset * 4);

    /// <summary>
    /// Wraps this memory handle as a non-owning temporary <see cref="GpuBigUInt"/> view without copying or allocating device memory.
    /// </summary>
    /// <param name="workspace">The execution workspace.</param>
    /// <returns>A non-owning <see cref="GpuBigUInt"/> view over this memory handle.</returns>
    internal GpuBigUInt AsBorrowedBigUInt(GpuWorkspace workspace) => new(workspace, this, isTemp: true);
}

