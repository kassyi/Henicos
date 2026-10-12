using System.Runtime.InteropServices;

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
    public IntPtr DevicePointer =>
        Handle.IsDefault ? IntPtr.Zero : IntPtr.Add(Handle.Buffer.Pointer, Handle.WordOffset * 4);

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
    /// <returns>A <see cref="GpuBigUInt"/> pointing to the element's device memory.</returns>
    public GpuBigUInt GetElement(int index)
    {
        if (index < 0 || index >= Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        var elementOffset = Handle.WordOffset + (index * WordsPerElement);
        var elementHandle = new GpuBigIntMemoryHandle(Handle.Buffer, elementOffset, WordsPerElement);
        return new GpuBigUInt(Workspace, elementHandle, isTemp: false);
    }

    /// <summary>
    /// Releases the GPU global device memory resources allocated for the underlying buffer.
    /// </summary>
    public void Dispose()
    {
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
