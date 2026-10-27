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
/// A handle belongs to one lease of its buffer. After the buffer has been freed (and possibly handed out again), the
/// handle is stale: reading its device pointer throws <see cref="ObjectDisposedException"/>, and freeing it again does
/// nothing. Equality, the hash code and <see cref="ToString"/> look at the handle only and never throw.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> As a readonly struct, instances are passed by value with no managed heap allocations.
/// </para>
/// </remarks>
public readonly struct GpuBigIntMemoryHandle : IEquatable<GpuBigIntMemoryHandle>
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
    /// Gets the generation of the lease of <see cref="Buffer"/> this handle belongs to.
    /// </summary>
    internal int Generation
    {
        get;
    }

    private GpuBigIntMemoryHandle(GpuBigIntDeviceBuffer buffer, int wordOffset, int numWords, int generation)
    {
        Buffer = buffer;
        WordOffset = wordOffset;
        NumWords = numWords;
        Generation = generation;
    }

    /// <summary>
    /// Creates the handle of a new lease of the whole <paramref name="buffer"/>; only the memory manager calls this.
    /// </summary>
    /// <param name="buffer">A buffer of the calling manager.</param>
    /// <returns>A handle of all words of <paramref name="buffer"/> at its current generation.</returns>
    internal static GpuBigIntMemoryHandle CreateOwned(GpuBigIntDeviceBuffer buffer) =>
        new(buffer, 0, buffer.TotalWords, buffer.Generation);

    /// <summary>
    /// Creates a handle of memory that belongs to someone outside GpuMath and is never freed here.
    /// </summary>
    /// <param name="buffer">A buffer without a manager.</param>
    /// <returns>A handle of all words of <paramref name="buffer"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="buffer"/> belongs to a memory manager.</exception>
    internal static GpuBigIntMemoryHandle CreateExternal(GpuBigIntDeviceBuffer buffer)
    {
        if (buffer.Manager is not null)
        {
            throw new ArgumentException("An external handle must not wrap a buffer of a memory manager.", nameof(buffer));
        }

        return new GpuBigIntMemoryHandle(buffer, 0, buffer.TotalWords, buffer.Generation);
    }

    /// <summary>
    /// Gets a value indicating whether this handle represents an uninitialized or default instance.
    /// </summary>
    public bool IsDefault => Buffer == null;

    /// <summary>
    /// Gets a value indicating whether the lease of this handle is still current; a default handle and a handle of
    /// external memory are always alive.
    /// </summary>
    internal bool IsAlive => IsDefault || Buffer.Manager is null || Generation == Buffer.Generation;

    /// <summary>
    /// Gets the raw 64-bit device memory pointer to the first limb word of this slice.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The buffer of this handle has been freed.</exception>
    internal IntPtr DevicePointer
    {
        get
        {
            if (IsDefault)
            {
                return IntPtr.Zero;
            }

            if (!IsAlive)
            {
                ThrowFreed();
            }

            return IntPtr.Add(Buffer.Pointer, WordOffset * 4);
        }
    }

    /// <summary>
    /// Returns a handle of the same words in the same lease, for code that uses the memory without freeing it.
    /// </summary>
    /// <returns>A copy of this handle.</returns>
    internal GpuBigIntMemoryHandle Borrow() => this;

    /// <summary>
    /// Returns a handle of <paramref name="numWords"/> words of this handle from word <paramref name="wordOffset"/>,
    /// in the same lease.
    /// </summary>
    /// <param name="wordOffset">The first word, relative to <see cref="WordOffset"/>.</param>
    /// <param name="numWords">The number of words.</param>
    /// <returns>The sub-handle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside this handle.</exception>
    internal GpuBigIntMemoryHandle Slice(int wordOffset, int numWords)
    {
        if (wordOffset < 0 || wordOffset > NumWords)
        {
            throw new ArgumentOutOfRangeException(nameof(wordOffset),
                $"The slice [{wordOffset}, {wordOffset} + {numWords}) is outside the {NumWords} words of the handle.");
        }

        if (numWords < 0 || numWords > NumWords - wordOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(numWords),
                $"The slice [{wordOffset}, {wordOffset} + {numWords}) is outside the {NumWords} words of the handle.");
        }

        return IsDefault ? this : new GpuBigIntMemoryHandle(Buffer, WordOffset + wordOffset, numWords, Generation);
    }

    /// <summary>
    /// Wraps this memory handle as a non-owning temporary <see cref="GpuBigUInt"/> view without copying or allocating device memory.
    /// </summary>
    /// <param name="workspace">The execution workspace.</param>
    /// <returns>A non-owning <see cref="GpuBigUInt"/> view over this memory handle.</returns>
    internal GpuBigUInt AsBorrowedBigUInt(GpuWorkspace workspace) => new(workspace, Borrow(), isTemp: true);

    /// <summary>
    /// Indicates whether two handles refer to the same words of the same lease of the same buffer.
    /// </summary>
    /// <param name="other">The handle to compare with.</param>
    /// <returns><see langword="true"/> if both handles are identical; otherwise, <see langword="false"/>.</returns>
    public bool Equals(GpuBigIntMemoryHandle other) =>
        ReferenceEquals(Buffer, other.Buffer) && WordOffset == other.WordOffset && NumWords == other.NumWords &&
        Generation == other.Generation;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is GpuBigIntMemoryHandle other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(RuntimeHelpers.GetHashCode(Buffer), WordOffset, NumWords, Generation);

    /// <summary>
    /// Returns the offset, the word count and the generation of the handle; never reads the device pointer.
    /// </summary>
    /// <returns>A string for debugging.</returns>
    public override string ToString() =>
        $"GpuBigIntMemoryHandle(WordOffset={WordOffset}, NumWords={NumWords}, Generation={Generation}, IsDefault={IsDefault})";

    /// <summary>
    /// Determines whether two handles are identical.
    /// </summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true"/> if the handles are identical; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right) => left.Equals(right);

    /// <summary>
    /// Determines whether two handles differ.
    /// </summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true"/> if the handles differ; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right) => !left.Equals(right);

    [DoesNotReturn]
    private static void ThrowFreed() =>
        throw new ObjectDisposedException(nameof(GpuBigIntMemoryHandle),
            "The device buffer of this handle has been freed (the value was disposed, or its workspace was disposed).");
}
