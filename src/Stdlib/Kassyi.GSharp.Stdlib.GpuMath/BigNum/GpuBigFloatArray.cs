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
    /// Gets a view of the specified element; reads its exponent and sign back from the device.
    /// </summary>
    /// <param name="index">The index of the element.</param>
    /// <returns>
    /// A borrowed view of the element's significand with its exponent and sign. It lives only inside the calling method
    /// (a <see langword="ref struct"/>) and must not be used after this array is disposed.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the array.</exception>
    public GpuBigFloatView GetElement(int index) => ((GpuBigFloatArrayView)this).GetElement(index);

    /// <summary>
    /// Copies the specified scalar <see cref="GpuBigFloat"/> value into the element at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The target element index.</param>
    /// <param name="value">The floating-point value to store; a <see cref="GpuBigFloat"/> converts implicitly.</param>
    /// <exception cref="OverflowException">The significand of <paramref name="value"/> is wider than the element and the
    /// exponent of the rounded value does not fit in <see cref="int"/>.</exception>
    /// <remarks>
    /// A significand of <see cref="WordsPerElement"/> words or fewer is copied as it is (zero-extended), so the element
    /// holds the value exactly. A wider significand is rounded toward zero to <c>32·WordsPerElement</c> bits, like the
    /// other <see cref="GpuBigFloat"/> operations (<see cref="IGpuBigFloatArithmetic.Normalize"/>): its top bit moves to
    /// the top of the element, the bits below the element are dropped and the exponent grows by the same shift; zero is
    /// stored as positive zero. Storing a wide value reads its bit length back to the host once.
    /// </remarks>
    public void SetElement(int index, scoped GpuBigFloatView value)
    {
        var destSig = Significands.GetElement(index);
        var destExp = Exponents.GetElement(index);
        var destSign = Signs.GetElement(index);

        var workspace = Workspace;
        var provider = workspace.Provider;
        var stream = workspace.Stream;
        var valueWords = value.Significand.NumWords;

        int exponent;
        bool isNegative;
        if (valueWords <= WordsPerElement)
        {
            if (valueWords < WordsPerElement)
            {
                provider.ClearMemoryAsync(destSig.DevicePointer, WordsPerElement, stream);
            }

            provider.CopyDeviceToDevice(destSig.DevicePointer, value.Significand.DevicePointer, valueWords, stream);
            exponent = value.Exponent;
            isNegative = value.IsNegative;
        }
        else
        {
            // Round toward zero to the element width (#629), with the arithmetic the operators use.
            var arithmetic = GpuWorkspaceArithmetic.FloatArithmetic(workspace);
            var context = GpuWorkspaceArithmetic.CreateContext(workspace);
            var operand = new GpuFloatOperand(value.Significand.Handle, value.Exponent, value.IsNegative);
            var rounded = arithmetic.Normalize(ref context, in operand, destSig.Handle);
            exponent = rounded.Exponent;
            isNegative = rounded.IsNegative;
        }

        var meta = ArrayPool<uint>.Shared.Rent(1);
        try
        {
            meta[0] = unchecked((uint)exponent);
            provider.CopyHostToDevice(destExp.DevicePointer, meta, 1, stream);

            meta[0] = isNegative ? 1u : 0u;
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
    /// <returns>
    /// A borrowed view of the elements. It lives only inside the calling method (a <see langword="ref struct"/>) and must
    /// not be used after this array is disposed.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside the array.</exception>
    public GpuBigFloatArrayView Slice(int elementOffset, int elementCount) =>
        ((GpuBigFloatArrayView)this).Slice(elementOffset, elementCount);

    /// <summary>
    /// Returns a range of elements as a <see cref="GpuBigFloatArray"/> that does not own the buffers, for the internal
    /// plumbing that needs the owned type; disposing it frees nothing.
    /// </summary>
    /// <param name="elementOffset">The starting element index.</param>
    /// <param name="elementCount">The number of elements.</param>
    /// <returns>A borrowed <see cref="GpuBigFloatArray"/>.</returns>
    internal GpuBigFloatArray BorrowSlice(int elementOffset, int elementCount) =>
        Slice(elementOffset, elementCount).AsBorrowed();

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
