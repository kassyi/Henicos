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
    /// Gets a scalar representation of the specified element (for debugging, involves slow device-to-host read).
    /// </summary>
    /// <param name="index">The index of the element.</param>
    /// <returns>A <see cref="GpuBigInt"/> pointing to the element's device memory.</returns>
    public GpuBigInt GetElement(int index)
    {
        var value = Values.GetElement(index);
        var signWord = Signs.GetElement(index);
        
        // Note: Reading from device memory to host is slow, this is just for debugging or single-element retrieval
        var isNegative = signWord.Workspace.Provider.ReadWord(signWord.DevicePointer, stream: signWord.Workspace.Stream) != 0;
        
        return new GpuBigInt(value, isNegative);
    }

    /// <summary>
    /// Creates a zero-allocation sub-slice of this GPU array.
    /// </summary>
    /// <param name="elementOffset">The starting element index of the slice.</param>
    /// <param name="elementCount">The number of elements in the slice.</param>
    /// <returns>A sliced view of the <see cref="GpuBigIntArray"/>.</returns>
    public GpuBigIntArray Slice(int elementOffset, int elementCount)
    {
        if (elementOffset < 0 || elementCount < 0 || elementOffset + elementCount > Length)
        {
            throw new ArgumentOutOfRangeException();
        }

        return new GpuBigIntArray(
            Values.Slice(elementOffset, elementCount),
            Signs.Slice(elementOffset, elementCount));
    }

    /// <summary>
    /// Releases the GPU global device memory resources allocated for the underlying buffers.
    /// </summary>
    public void Dispose()
    {
        Values.Dispose();
        Signs.Dispose();
    }
}
