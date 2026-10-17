namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents a contiguous array of arbitrary-precision signed floating-point numbers stored in NVIDIA GPU global device memory.
/// </summary>
public readonly struct GpuBigFloatArray : IDisposable
{
    /// <summary>
    /// Gets the underlying contiguous array of unsigned significand magnitudes.
    /// </summary>
    public GpuBigUIntArray Significands { get; }

    /// <summary>
    /// Gets the underlying contiguous array of base-2 exponents (1 32-bit word per element, stored as two's complement <see cref="int"/> bits).
    /// </summary>
    public GpuBigUIntArray Exponents { get; }

    /// <summary>
    /// Gets the underlying contiguous array of sign indicators (1 word per element, 0 = positive, 1 = negative).
    /// </summary>
    public GpuBigUIntArray Signs { get; }

    /// <summary>
    /// Gets the <see cref="GpuWorkspace"/> associated with this GPU array instance.
    /// </summary>
    public GpuWorkspace Workspace => Significands.Workspace;

    /// <summary>
    /// Gets the length of the array (number of elements).
    /// </summary>
    public int Length => Significands.Length;

    /// <summary>
    /// Gets the number of 32-bit significand words per element.
    /// </summary>
    public int WordsPerElement => Significands.WordsPerElement;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigFloatArray"/> struct allocating uninitialized GPU device memory.
    /// </summary>
    /// <param name="workspace">The GPU workspace managing memory allocations.</param>
    /// <param name="length">The number of elements in the array.</param>
    /// <param name="wordsPerElement">The number of 32-bit significand words per element.</param>
    public GpuBigFloatArray(GpuWorkspace workspace, int length, int wordsPerElement)
    {
        Significands = new GpuBigUIntArray(workspace, length, wordsPerElement);
        try
        {
            Exponents = new GpuBigUIntArray(workspace, length, 1);
            try
            {
                Signs = new GpuBigUIntArray(workspace, length, 1);
            }
            catch
            {
                Exponents.Dispose();
                throw;
            }
        }
        catch
        {
            Significands.Dispose();
            throw;
        }
    }

    internal GpuBigFloatArray(
        [TakesOwnership] GpuBigUIntArray significands,
        [TakesOwnership] GpuBigUIntArray exponents,
        [TakesOwnership] GpuBigUIntArray signs)
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
    /// Gets a scalar representation of the specified element.
    /// </summary>
    /// <param name="index">The index of the element.</param>
    /// <returns>A <see cref="GpuBigFloat"/> pointing to the element's device memory.</returns>
    public GpuBigFloat GetElement(int index)
    {
        var sig = Significands.GetElement(index);
        var expWord = Exponents.GetElement(index);
        var signWord = Signs.GetElement(index);

        var provider = sig.Workspace.Provider;
        var stream = sig.Workspace.Stream;
        var exp = unchecked((int)provider.ReadWord(expWord.DevicePointer, stream: stream));
        var isNegative = provider.ReadWord(signWord.DevicePointer, stream: stream) != 0;

        return new GpuBigFloat(sig, exp, isNegative);
    }

    /// <summary>
    /// Copies the specified scalar <see cref="GpuBigFloat"/> value into the element at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The target element index.</param>
    /// <param name="value">The floating-point value to store.</param>
    public void SetElement(int index, GpuBigFloat value)
    {
        var destSig = Significands.GetElement(index);
        var destExp = Exponents.GetElement(index);
        var destSign = Signs.GetElement(index);

        var workspace = Workspace;
        var provider = workspace.Provider;
        var stream = workspace.Stream;

        if (value.UFloat.Significand.NumWords < WordsPerElement)
        {
            provider.ClearMemoryAsync(destSig.DevicePointer, WordsPerElement, stream);
        }

        var wordsToCopy = Math.Min(value.UFloat.Significand.NumWords, WordsPerElement);
        provider.CopyDeviceToDevice(destSig.DevicePointer, value.UFloat.Significand.DevicePointer, wordsToCopy, stream);

        var meta = ArrayPool<uint>.Shared.Rent(1);
        try
        {
            meta[0] = unchecked((uint)value.UFloat.Exponent);
            provider.CopyHostToDevice(destExp.DevicePointer, meta, 1, stream);

            meta[0] = value.IsNegative ? 1u : 0u;
            provider.CopyHostToDevice(destSign.DevicePointer, meta, 1, stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(meta);
        }
    }

    /// <summary>
    /// Creates a zero-allocation sub-slice of this GPU floating-point array.
    /// </summary>
    /// <param name="elementOffset">The starting element index of the slice.</param>
    /// <param name="elementCount">The number of elements in the slice.</param>
    /// <returns>A sliced view of the <see cref="GpuBigFloatArray"/>.</returns>
    public GpuBigFloatArray Slice(int elementOffset, int elementCount)
    {
        if (elementOffset < 0 || elementCount < 0 || elementOffset + elementCount > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        return new GpuBigFloatArray(
            Significands.Slice(elementOffset, elementCount),
            Exponents.Slice(elementOffset, elementCount),
            Signs.Slice(elementOffset, elementCount));
    }

    /// <summary>
    /// Releases the GPU global device memory resources allocated for the underlying buffers.
    /// </summary>
    public void Dispose()
    {
        Significands.Dispose();
        Exponents.Dispose();
        Signs.Dispose();
    }
}
