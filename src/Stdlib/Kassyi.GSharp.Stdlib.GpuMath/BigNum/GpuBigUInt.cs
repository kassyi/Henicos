

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents an arbitrary-precision unsigned integer stored in NVIDIA GPU global device memory.
/// </summary>
/// <remarks>
/// <para><b>Performance:</b> Backed by GPU device memory handles allocated from <see cref="GpuWorkspace.MemoryManager"/>.</para>
/// <para><b>Memory Management:</b> Implements <see cref="IDisposable"/> to return allocated GPU device memory to the pool.</para>
/// <para><b>Expression Trees:</b> Supports deferred expression compilation and operator fusion via overloaded arithmetic operators.</para>
/// <para><b>Thread Safety:</b> Value-type wrapper semantics; safe for concurrent reads across threads when distinct instances are used.</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
[GenerateGpuNumberOperators]
public readonly partial struct GpuBigUInt : IDisposable, IGpuBigUIntExpression, IGpuNumber<GpuBigUInt>
{
    /// <summary>
    /// Gets the <see cref="GpuWorkspace"/> associated with this GPU integer instance.
    /// </summary>
    public GpuWorkspace Workspace { get; }

    /// <summary>
    /// Gets the underlying GPU memory handle tracking device buffer allocation and lifetime.
    /// </summary>
    public GpuBigIntMemoryHandle Handle { get; }

    /// <summary>
    /// Gets the raw 64-bit device memory pointer to the first limb word in GPU global address space.
    /// </summary>
    public IntPtr DevicePointer =>
        Handle.IsDefault ? IntPtr.Zero : IntPtr.Add(Handle.Buffer.Pointer, Handle.WordOffset * 4);

    /// <summary>
    /// Gets the number of 32-bit unsigned integer limbs (words) allocated for this big integer.
    /// </summary>
    public int NumWords => Handle.IsDefault ? 0 : Handle.NumWords;

    /// <summary>
    /// Returns a native array proxy struct representing this GPU buffer for P/Invoke AOT context structs.
    /// </summary>
    /// <returns>A native array proxy struct.</returns>
    public GSharpArrayProxy ToProxy() => new() { Data = DevicePointer, Length = NumWords };

    /// <summary>
    /// Implicitly converts this <see cref="GpuBigUInt"/> to <see cref="GSharpArrayProxy"/>.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <remarks>
    /// This provides a convenient syntax for passing GPU big integer buffers to native marshal context structs.
    /// </remarks>
    public static implicit operator GSharpArrayProxy(GpuBigUInt value) => value.ToProxy();

    /// <summary>
    /// Gets a value indicating whether this buffer represents an intermediate temporary buffer managed by expression evaluation.
    /// </summary>
    public bool IsTemp { get; }

    internal GpuBigUInt(GpuWorkspace workspace, GpuBigIntMemoryHandle handle, bool isTemp = false)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Handle = handle;
        IsTemp = isTemp;
    }

    internal GpuBigUInt(GpuWorkspace workspace, int numWords, IntPtr preAllocatedPointer, bool isTemp = false)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        var buffer = new GpuBigIntDeviceBuffer(preAllocatedPointer, numWords);
        Handle = new GpuBigIntMemoryHandle(buffer, 0, numWords);
        IsTemp = isTemp;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigUInt"/> struct allocating uninitialized GPU device memory.
    /// </summary>
    /// <param name="workspace">The GPU workspace managing memory allocations and kernel dispatch.</param>
    /// <param name="numWords">The number of 32-bit words to allocate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="workspace"/> is <see langword="null"/>.</exception>
    public GpuBigUInt(GpuWorkspace workspace, int numWords)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Handle = Workspace.MemoryManager.Allocate(numWords);
        IsTemp = false;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigUInt"/> struct initialized with data copied from host memory.
    /// </summary>
    /// <param name="workspace">The GPU workspace managing memory allocations and kernel dispatch.</param>
    /// <param name="hostData">An array of 32-bit limb words in little-endian order to copy to device memory.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="workspace"/> or <paramref name="hostData"/> is <see langword="null"/>.</exception>
    public GpuBigUInt(GpuWorkspace workspace, uint[] hostData)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        ArgumentNullException.ThrowIfNull(hostData);
        Handle = Workspace.MemoryManager.Allocate(hostData.Length);
        IsTemp = false;
        Workspace.Provider.CopyHostToDevice(DevicePointer, hostData, NumWords, Workspace.Stream);
    }

    /// <summary>
    /// Gets a deferred expression representing this instance, or evaluates and assigns a deferred expression into this instance in-place.
    /// </summary>
    public GpuBigUIntExpr Value
    {
        get => new(this);
        set => value.Node.Evaluate(Workspace, this);
    }

    /// <summary>
    /// Creates a deferred expression representing the bitwise NOT (one's complement) of an unsigned big integer.
    /// </summary>
    /// <param name="leftOperand">The operand to invert.</param>
    /// <returns>A <see cref="GpuBigUIntExpr"/> representing the bitwise NOT operation.</returns>
    public static GpuBigUIntExpr operator ~(GpuBigUInt leftOperand) =>
        new(new BitwiseNotExpression(leftOperand));

    /// <summary>
    /// Increments the specified <see cref="GpuBigUInt"/> by 1 on the GPU in-place.
    /// </summary>
    /// <param name="operand">The big integer to increment.</param>
    /// <returns>The incremented <see cref="GpuBigUInt"/> instance.</returns>
    public static GpuBigUInt operator ++(GpuBigUInt operand)
    {
        GpuBigIntMath.Add(operand, 1u, operand);
        return operand;
    }

    /// <summary>
    /// Decrements the specified <see cref="GpuBigUInt"/> by 1 on the GPU in-place.
    /// </summary>
    /// <param name="operand">The big integer to decrement.</param>
    /// <returns>The decremented <see cref="GpuBigUInt"/> instance.</returns>
    public static GpuBigUInt operator --(GpuBigUInt operand)
    {
        GpuBigIntMath.Sub(operand, 1u, operand);
        return operand;
    }


    /// <summary>
    /// Copies all limb words from GPU device memory to a newly allocated host array.
    /// </summary>
    /// <returns>An array of 32-bit unsigned integers representing the limbs in little-endian order.</returns>
    public uint[] ToHostArray()
    {
        var hostData = new uint[NumWords];
        Workspace.Provider.CopyDeviceToHost(hostData, DevicePointer, NumWords, Workspace.Stream);
        return hostData;
    }

    /// <summary>
    /// Clears (zeros out) a range of limb words in GPU device memory.
    /// </summary>
    /// <param name="offsetWords">The word offset within this buffer from which to start clearing.</param>
    /// <param name="numWords">The number of words to clear, or <see langword="null"/> to clear until the end of the buffer.</param>
    public void Clear(int offsetWords = 0, int? numWords = null)
    {
        var wordsToClear = numWords ?? (NumWords - offsetWords);
        if (wordsToClear <= 0)
        {
            return;
        }

        var zeroArray = ArrayPool<uint>.Shared.Rent(wordsToClear);
        try
        {
            Array.Clear(zeroArray, 0, wordsToClear);
            Workspace.Provider.CopyHostToDevice(IntPtr.Add(DevicePointer, offsetWords * 4), zeroArray, wordsToClear,
                Workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(zeroArray);
        }
    }

    /// <summary>
    /// Copies limb words from another <see cref="GpuBigUInt"/> instance to this instance in GPU device memory.
    /// </summary>
    /// <param name="source">The source <see cref="GpuBigUInt"/> buffer.</param>
    /// <param name="destOffsetWords">The word offset within this destination buffer.</param>
    /// <param name="srcOffsetWords">The word offset within the source buffer.</param>
    /// <param name="numWords">The number of words to copy, or <see langword="null"/> to copy as many words as fit.</param>
    public void CopyFrom(GpuBigUInt source, int destOffsetWords = 0, int srcOffsetWords = 0,
        int? numWords = null)
    {
        var wordsToCopy = numWords ?? Math.Min(NumWords - destOffsetWords, source.NumWords - srcOffsetWords);
        if (wordsToCopy <= 0)
        {
            return;
        }

        var destPtr = IntPtr.Add(DevicePointer, destOffsetWords * 4);
        var srcPtr = IntPtr.Add(source.DevicePointer, srcOffsetWords * 4);
        Workspace.Provider.CopyDeviceToDevice(destPtr, srcPtr, wordsToCopy, Workspace.Stream);
    }

    /// <summary>
    /// Copies the most significant limb words from GPU device memory to a host array.
    /// </summary>
    /// <param name="wordCount">The number of top words to copy.</param>
    /// <returns>A host array containing the most significant limb words.</returns>
    public uint[] GetTopWords(int wordCount)
    {
        if (wordCount > NumWords)
        {
            wordCount = NumWords;
        }

        var hostData = new uint[wordCount];
        var srcPtr = IntPtr.Add(DevicePointer, (NumWords - wordCount) * 4);
        Workspace.Provider.CopyDeviceToHost(hostData, srcPtr, wordCount, Workspace.Stream);
        return hostData;
    }

    /// <summary>
    /// Compares this instance to another <see cref="GpuBigUInt"/> and returns an integer indicating their relative magnitude.
    /// </summary>
    /// <param name="other">The <see cref="GpuBigUInt"/> to compare with this instance.</param>
    /// <returns>
    /// A signed integer that indicates the relative values of this instance and <paramref name="other"/>:
    /// Less than zero if this instance is less than <paramref name="other"/>,
    /// Zero if this instance equals <paramref name="other"/>,
    /// Greater than zero if this instance is greater than <paramref name="other"/>.
    /// </returns>
    public int CompareTo(GpuBigUInt other) => Workspace.Provider.CompareTo(Workspace, this, other);

    /// <summary>
    /// Determines whether the specified object is equal to the current instance.
    /// </summary>
    /// <param name="obj">The object to compare with the current instance.</param>
    /// <returns><see langword="true"/> if <paramref name="obj"/> is a <see cref="GpuBigUInt"/> and equals the current instance; otherwise, <see langword="false"/>.</returns>
    public override bool Equals(object? obj) => obj is GpuBigUInt other && Equals(other);

    /// <summary>
    /// Indicates whether the current instance is equal to another <see cref="GpuBigUInt"/> instance.
    /// </summary>
    /// <param name="other">A <see cref="GpuBigUInt"/> to compare with this instance.</param>
    /// <returns><see langword="true"/> if the current instance is equal to the <paramref name="other"/> parameter; otherwise, <see langword="false"/>.</returns>
    public bool Equals(GpuBigUInt other) =>
        Handle.Equals(other.Handle) || (Workspace != null && CompareTo(other) == 0);

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code.</returns>
    public override int GetHashCode() => Handle.GetHashCode();

    /// <summary>
    /// Returns a string representation of the <see cref="GpuBigUInt"/> instance for debugging.
    /// </summary>
    /// <returns>A string containing word count and device pointer address.</returns>
    public override string ToString() => $"GpuBigUInt(Words={NumWords}, DevicePointer=0x{DevicePointer:X})";

    /// <summary>
    /// Clears a single 32-bit limb word at the specified word index in GPU device memory.
    /// </summary>
    /// <param name="wordIndex">The 0-based word index to clear.</param>
    public void ClearWord(int wordIndex)
    {
        var destPtr = IntPtr.Add(DevicePointer, wordIndex * 4);
        Workspace.Provider.CopyHostToDevice(destPtr, [0], 1, Workspace.Stream);
    }

    /// <summary>
    /// Creates a new <see cref="GpuBigUInt"/> initialized to $2^{\text{bitIndex}}$ ($1 \ll \text{bitIndex}$).
    /// </summary>
    /// <param name="workspace">The GPU workspace managing memory and device copy.</param>
    /// <param name="numWords">The total word capacity of the resulting big integer.</param>
    /// <param name="bitIndex">The 0-based bit position to set to 1.</param>
    /// <returns>A newly allocated <see cref="GpuBigUInt"/> representing $2^{\text{bitIndex}}$.</returns>
    public static GpuBigUInt CreatePowerOfTwo(GpuWorkspace workspace, int numWords, int bitIndex)
    {
        var result = new GpuBigUInt(workspace, numWords);
        var wordIndex = bitIndex / 32;
        var bitOffset = bitIndex % 32;
        if (wordIndex >= numWords)
        {
            return result;
        }

        var word = ArrayPool<uint>.Shared.Rent(1);
        try
        {
            word[0] = 1u << bitOffset;
            workspace.Provider.CopyHostToDevice(IntPtr.Add(result.DevicePointer, wordIndex * 4), word, 1,
                workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(word);
        }

        return result;
    }

    /// <summary>
    /// Releases the underlying GPU global device memory handle back to the workspace memory pool.
    /// </summary>
    public void Dispose()
    {
        if (Workspace != null && !Handle.IsDefault && !IsTemp)
        {
            Workspace.MemoryManager.Free(Handle);
        }
    }

    /// <summary>
    /// Determines whether the value of this instance is 0 (zero).
    /// </summary>
    /// <returns><see langword="true"/> if all limb words equal 0; otherwise, <see langword="false"/>.</returns>
    public bool IsZero() => Workspace.Provider.IsZero(Workspace, this);

    /// <summary>
    /// Determines whether the value of this instance is 1 (one).
    /// </summary>
    /// <returns><see langword="true"/> if the value equals 1; otherwise, <see langword="false"/>.</returns>
    public bool IsOne() => Workspace.Provider.IsOne(Workspace, this);

    /// <summary>
    /// Calculates the minimum number of bits required to represent this big integer without leading zeros.
    /// </summary>
    /// <returns>The bit length of the number, or 0 if the value is zero.</returns>
    public int GetBitLength() => Workspace.Provider.GetBitLength(Workspace, this);

    /// <summary>
    /// Calculates the minimum number of bytes required to represent this big integer.
    /// </summary>
    /// <returns>The byte length of the number, or 1 if the value is zero.</returns>
    public int GetByteCount()
    {
        var bitLength = GetBitLength();
        return bitLength == 0 ? 1 : (bitLength + 7) / 8;
    }

    /// <summary>
    /// Determines whether the value is an even integer.
    /// </summary>
    /// <returns><see langword="true"/> if the least significant bit is 0; otherwise, <see langword="false"/>.</returns>
    public bool GetIsEven()
    {
        var word = Workspace.Provider.ReadWord(DevicePointer);
        return (word & 1) == 0;
    }

    /// <summary>
    /// Determines whether the most significant bit in the highest word is set (used for two's complement sign tracking).
    /// </summary>
    /// <returns><see langword="true"/> if the MSB of the highest word is set; otherwise, <see langword="false"/>.</returns>
    public bool GetIsNegative()
    {
        var word = Workspace.Provider.ReadWord(DevicePointer, NumWords - 1);
        return (word & 0x80000000) != 0;
    }

    /// <summary>
    /// Sets or clears the most significant bit in the highest word (used for two's complement sign tracking).
    /// </summary>
    /// <param name="value"><see langword="true"/> to set the sign bit; <see langword="false"/> to clear it.</param>
    public void SetIsNegative(bool value)
    {
        var word = Workspace.Provider.ReadWord(DevicePointer, NumWords - 1);
        if (value)
        {
            word |= 0x80000000;
        }
        else
        {
            word &= 0x7FFFFFFF;
        }

        Workspace.Provider.WriteWord(DevicePointer, NumWords - 1, word);
    }

    /// <summary>
    /// Computes the square of this number in-place on the GPU.
    /// </summary>
    /// <param name="logicalWords">The logical number of limb words in the input operand.</param>
    /// <returns><see langword="true"/> if the square operation succeeded; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="NumWords"/> is less than <c>logicalWords * 2</c>.</exception>
    public bool Square(int logicalWords)
    {
        if (NumWords < logicalWords * 2)
        {
            throw new InvalidOperationException(
                $"Square requires the destination buffer to have at least {logicalWords * 2} words, but only has {NumWords}.");
        }

        return Workspace.Provider.Square(Workspace, DevicePointer, logicalWords);
    }

    /// <summary>
    /// Allocates a new <see cref="GpuBigUInt"/> with the specified target word capacity and copies this instance's limbs into it.
    /// </summary>
    /// <param name="targetWords">The target word capacity of the new buffer.</param>
    /// <returns>A new <see cref="GpuBigUInt"/> padded or truncated to <paramref name="targetWords"/>.</returns>
    public GpuBigUInt PadTo(int targetWords)
    {
        var result = new GpuBigUInt(Workspace, targetWords);
        var copyWords = Math.Min(NumWords, targetWords);
        Workspace.Provider.CopyDeviceToDevice(result.DevicePointer, DevicePointer, copyWords, Workspace.Stream);
        return result;
    }

    /// <summary>
    /// Creates a new <see cref="GpuBigUInt"/> instance from a .NET standard <see cref="BigInteger"/> value.
    /// </summary>
    /// <param name="workspace">The GPU workspace managing memory allocations.</param>
    /// <param name="val">The <see cref="BigInteger"/> value to convert.</param>
    /// <param name="minWords">The minimum word capacity to allocate on the GPU.</param>
    /// <returns>A newly allocated <see cref="GpuBigUInt"/> containing the magnitude of <paramref name="val"/>.</returns>
    public static GpuBigUInt FromBigInteger(GpuWorkspace workspace, BigInteger val, int minWords = 0)
    {
        val = BigInteger.Abs(val);
        if (val.IsZero)
        {
            return new GpuBigUInt(workspace, Math.Max(1, minWords));
        }

        var byteCount = val.GetByteCount(isUnsigned: true);
        var words = (byteCount + 3) / 4;
        var allocWords = Math.Max(words, minWords);
        var hostData = ArrayPool<uint>.Shared.Rent(allocWords);
        try
        {
            hostData.AsSpan(0, allocWords).Clear();
            var byteSpan = MemoryMarshal.AsBytes(hostData.AsSpan());
            val.TryWriteBytes(byteSpan, out _, isUnsigned: true, isBigEndian: false);
            var result = new GpuBigUInt(workspace, allocWords);
            workspace.Provider.CopyHostToDevice(result.DevicePointer, hostData, allocWords, workspace.Stream);
            return result;
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostData);
        }
    }

    /// <summary>
    /// Copies limb data from GPU device memory and reconstructs a .NET standard <see cref="BigInteger"/>.
    /// </summary>
    /// <param name="isNegative"><see langword="true"/> to return a negative <see cref="BigInteger"/>; otherwise, <see langword="false"/>.</param>
    /// <returns>A .NET <see cref="BigInteger"/> representing the value stored in GPU memory.</returns>
    public BigInteger ToBigInteger(bool isNegative = false)
    {
        var hostData = ArrayPool<uint>.Shared.Rent(NumWords);
        try
        {
            Workspace.Provider.CopyDeviceToHost(hostData, DevicePointer, NumWords, Workspace.Stream);
            var byteSpan = MemoryMarshal.AsBytes(hostData.AsSpan(0, NumWords));
            var big = new BigInteger(byteSpan, isUnsigned: true, isBigEndian: false);
            return isNegative ? -big : big;
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostData);
        }
    }

    /// <summary>
    /// Computes the modular multiplicative inverse of this instance modulo <paramref name="modulus"/>.
    /// </summary>
    /// <param name="modulus">The modulus operand.</param>
    /// <returns>A new <see cref="GpuBigUInt"/> containing the modular inverse ($x \equiv \text{this}^{-1} \pmod{\text{modulus}}$).</returns>
    public GpuBigUInt ModInverse(GpuBigUInt modulus)
    {
        var result = new GpuBigUInt(Workspace, NumWords);
        GpuBigIntMath.ModInverse(this, modulus, result);
        return result;
    }

    /// <summary>
    /// Dispatches the visitor to evaluate or process this <see cref="GpuBigUInt"/> node in an expression tree.
    /// </summary>
    /// <param name="visitor">The expression visitor instance.</param>
    /// <returns>A <see cref="ScopedBigIntMemory"/> wrapping the evaluated memory handle.</returns>
    public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) =>
        visitor.VisitGpuUIntBigInteger(this);
}
