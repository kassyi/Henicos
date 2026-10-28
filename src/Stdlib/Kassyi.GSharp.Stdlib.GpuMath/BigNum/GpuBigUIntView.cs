namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A borrowed view of an arbitrary-precision unsigned integer in GPU device memory: an element of a
/// <see cref="GpuBigUIntArray"/>, a range of words, or a <see cref="GpuBigUInt"/> passed to an operation.
/// </summary>
/// <remarks>
/// <para>
/// Like <see cref="Span{T}"/>, the view is a <see langword="ref struct"/>: it lives only on the stack, inside the call
/// that obtained it. The compiler rejects storing it in a field of a class or a non-ref struct, capturing it in a lambda,
/// boxing it, using it as a generic type argument or an array element, and keeping it across an <see langword="await"/>.
/// To keep the value, copy it with <see cref="ToOwned"/>.
/// </para>
/// <para>
/// The view does not own its buffer and has no <c>Dispose</c>. It must not be used after its owner is disposed: its
/// <see cref="DevicePointer"/> then throws <see cref="ObjectDisposedException"/>, also inside the same method (C# does not
/// check this at compile time, as it does not for a <see cref="Span{T}"/> over an array returned to a pool).
/// </para>
/// <para>
/// A <see cref="GpuBigUInt"/> converts to a view implicitly, as an array does to a <see cref="Span{T}"/>, so operations
/// that take views accept owned values unchanged. A view has no expression operators; build expressions from owned
/// values.
/// </para>
/// </remarks>
public readonly ref struct GpuBigUIntView
{
    internal GpuBigUIntView(GpuWorkspace workspace, GpuBigIntMemoryHandle handle)
    {
        Workspace = workspace;
        Handle = handle.Borrow();
    }

    /// <summary>
    /// Implicitly converts an owned (or borrowed) <see cref="GpuBigUInt"/> to a view of all its words.
    /// </summary>
    /// <param name="value">The value to view.</param>
    public static implicit operator GpuBigUIntView(GpuBigUInt value) => new(value.Workspace, value.Handle);

    /// <summary>
    /// Gets the borrowed handle of the viewed words (never owning).
    /// </summary>
    internal GpuBigIntMemoryHandle Handle { get; }

    /// <summary>
    /// Gets the <see cref="GpuWorkspace"/> of the viewed buffer, or <see langword="null"/> for an empty view.
    /// </summary>
    public GpuWorkspace Workspace { get; }

    /// <summary>
    /// Gets the number of 32-bit words (limbs) in the view.
    /// </summary>
    public int NumWords => Handle.IsDefault ? 0 : Handle.NumWords;

    /// <summary>
    /// Gets the raw 64-bit device memory pointer to the first word of the view.
    /// </summary>
    /// <remarks>
    /// The pointer itself is not tracked, like the pointer of a pinned <see cref="Memory{T}"/>: it is checked when it is
    /// read, but a copy of it must not be used after the owner is disposed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The owner of the buffer has been disposed.</exception>
    public IntPtr DevicePointer => Handle.DevicePointer;

    /// <summary>
    /// Gets a value indicating whether this is the empty view (<see langword="default"/>), which refers to no buffer.
    /// </summary>
    public bool IsEmpty => Handle.IsDefault;

    /// <summary>
    /// Returns a view of <paramref name="numWords"/> words of this view from word <paramref name="wordOffset"/>.
    /// </summary>
    /// <param name="wordOffset">The first word, relative to this view.</param>
    /// <param name="numWords">The number of words.</param>
    /// <returns>The narrower view.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside this view.</exception>
    public GpuBigUIntView Slice(int wordOffset, int numWords) => new(Workspace, Handle.Slice(wordOffset, numWords));

    /// <summary>
    /// Returns the view as a <see cref="GpuBigUInt"/> that does not own the buffer, for the internal plumbing (providers,
    /// handlers) that works on <see cref="GpuBigUInt"/>; disposing it frees nothing.
    /// </summary>
    /// <returns>A borrowed <see cref="GpuBigUInt"/> of the same words.</returns>
    internal GpuBigUInt AsBorrowedBigUInt() => new(Workspace, Handle);

    /// <summary>
    /// Copies the viewed words into a new buffer that the caller owns.
    /// </summary>
    /// <returns>
    /// A new <see cref="GpuBigUInt"/> with the same value; dispose it. The empty view returns <see langword="default"/>,
    /// as <see cref="Span{T}.ToArray"/> of an empty span returns an empty array.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The owner of the buffer has been disposed.</exception>
    public GpuBigUInt ToOwned()
    {
        if (IsEmpty)
        {
            return default;
        }

        var owned = new GpuBigUInt(Workspace, NumWords);
        try
        {
            Workspace.Provider.CopyDeviceToDevice(owned.DevicePointer, DevicePointer, NumWords, Workspace.Stream);
        }
        catch
        {
            owned.Dispose();
            throw;
        }

        return owned;
    }

    /// <summary>
    /// Copies the value into <paramref name="destination"/> and clears its words above the value.
    /// </summary>
    /// <param name="destination">A view at least as wide as this view.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is narrower than this view.</exception>
    public void CopyTo(scoped GpuBigUIntView destination)
    {
        if (destination.NumWords < NumWords)
        {
            throw new ArgumentException(
                $"The destination has {destination.NumWords} words, fewer than the {NumWords} words of the source.",
                nameof(destination));
        }

        var provider = Workspace.Provider;
        var stream = Workspace.Stream;
        provider.CopyDeviceToDevice(destination.DevicePointer, DevicePointer, NumWords, stream);
        if (destination.NumWords > NumWords)
        {
            provider.ClearMemoryAsync(IntPtr.Add(destination.DevicePointer, NumWords * 4), destination.NumWords - NumWords,
                stream);
        }
    }

    /// <summary>
    /// Returns a native array proxy of the viewed words for P/Invoke AOT context structs.
    /// </summary>
    /// <remarks>
    /// The conversion is explicit only: an implicit conversion would let <c>object o = view;</c> compile by boxing the
    /// proxy, and the raw pointer would leave the call without the generation check.
    /// </remarks>
    /// <returns>A native array proxy struct.</returns>
    public GSharpArrayProxy ToProxy() => new() { Data = DevicePointer, Length = NumWords };

    /// <inheritdoc cref="GpuBigUInt.ToHostArray"/>
    public uint[] ToHostArray() => AsBorrowedBigUInt().ToHostArray();

    /// <inheritdoc cref="GpuBigUInt.Clear"/>
    public void Clear(int offsetWords = 0, int? numWords = null) => AsBorrowedBigUInt().Clear(offsetWords, numWords);

    /// <inheritdoc cref="GpuBigUInt.ClearWord"/>
    public void ClearWord(int wordIndex) => AsBorrowedBigUInt().ClearWord(wordIndex);

    /// <inheritdoc cref="GpuBigUInt.CopyFrom"/>
    public void CopyFrom(scoped GpuBigUIntView source, int destOffsetWords = 0, int srcOffsetWords = 0,
        int? numWords = null) =>
        AsBorrowedBigUInt().CopyFrom(source, destOffsetWords, srcOffsetWords, numWords);

    /// <inheritdoc cref="GpuBigUInt.GetTopWords"/>
    public uint[] GetTopWords(int wordCount) => AsBorrowedBigUInt().GetTopWords(wordCount);

    /// <summary>
    /// Compares the value of this view with the value of another view on the GPU.
    /// </summary>
    /// <param name="other">The view to compare with.</param>
    /// <returns>Less than zero, zero or greater than zero as this value is less than, equal to or greater than <paramref name="other"/>.</returns>
    public int CompareTo(scoped GpuBigUIntView other) => AsBorrowedBigUInt().CompareTo(other);

    /// <inheritdoc cref="GpuBigUInt.IsZero"/>
    public bool IsZero() => AsBorrowedBigUInt().IsZero();

    /// <inheritdoc cref="GpuBigUInt.IsOne"/>
    public bool IsOne() => AsBorrowedBigUInt().IsOne();

    /// <inheritdoc cref="GpuBigUInt.GetBitLength"/>
    public int GetBitLength() => AsBorrowedBigUInt().GetBitLength();

    /// <inheritdoc cref="GpuBigUInt.GetByteCount"/>
    public int GetByteCount() => AsBorrowedBigUInt().GetByteCount();

    /// <inheritdoc cref="GpuBigUInt.GetIsEven"/>
    public bool GetIsEven() => AsBorrowedBigUInt().GetIsEven();

    /// <inheritdoc cref="GpuBigUInt.PadTo"/>
    public GpuBigUInt PadTo(int targetWords) => AsBorrowedBigUInt().PadTo(targetWords);

    /// <inheritdoc cref="GpuBigUInt.ToBigInteger"/>
    public BigInteger ToBigInteger(bool isNegative = false) => AsBorrowedBigUInt().ToBigInteger(isNegative);

    /// <inheritdoc cref="GpuBigUInt.ModInverse"/>
    public GpuBigUInt ModInverse(scoped GpuBigUIntView modulus) => AsBorrowedBigUInt().ModInverse(modulus);

    /// <inheritdoc cref="GpuBigUInt.DivideByUInt32"/>
    public (GpuBigUInt Quotient, uint Remainder) DivideByUInt32(uint divisor) =>
        AsBorrowedBigUInt().DivideByUInt32(divisor);

    /// <summary>
    /// Returns the word count and the handle of the view; it does not read device memory.
    /// </summary>
    /// <returns>A string for debugging.</returns>
    public override string ToString() => $"GpuBigUIntView(Words={NumWords}, {Handle})";
}
