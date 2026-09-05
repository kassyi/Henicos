using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Represents an arbitrary-precision unsigned integer stored in NVIDIA GPU global device memory.
/// </summary>
/// <remarks>
/// <para>All arithmetic and bitwise operations are executed on the GPU using SIMD/warp-level intrinsics.</para>
/// <para><b>Memory Management:</b> Implements <see cref="IDisposable"/> to ensure deterministic GPU memory deallocation.</para>
/// </remarks>
/// <remarks><para>Implements: <b>GEN-46, GEN-48</b></para></remarks>
/// <remarks><para>Implements: <b>GBI-01, GBI-02, GBI-03, GBI-04, GBI-05, GBI-06, GBI-10, GBI-11, GBI-12, GBI-13, GBI-14, GBI-15, GBI-16, GBI-17, GBI-18, GBI-19, GBI-20, GBI-21, GBI-22, GBI-23, GBI-24, GBI-25, GBI-26, GBI-27, GBI-28, GBI-30, GBI-31, GBI-32, GBI-33, GBI-34</b></para></remarks>
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
public class GpuBigInteger : IDisposable, IGpuBigIntExpression
{
    /// <summary>
    /// Gets the native GPU global device memory pointer (<c>CUdeviceptr</c>) holding the integer limb data.
    /// </summary>
    public IntPtr DevicePointer
    {
        get;
    }

    /// <summary>
    /// Gets the total number of 32-bit words (limbs) allocated for this big integer.
    /// </summary>
    public int NumWords
    {
        get;
    }

    /// <summary>
    /// Gets the parent <see cref="GpuContext"/> associated with this instance.
    /// </summary>
    public GpuContext Context
    {
        get;
    }

    internal bool IsTemp
    {
        get; set;
    }

    internal bool _disposed;

    /// <summary>
    /// Allocates uninitialized GPU device memory for an integer of the specified limb length.
    /// </summary>
    /// <param name="context">The parent GPU execution context.</param>
    /// <param name="numWords">The number of 32-bit limbs to allocate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="OutOfMemoryException">Thrown when device memory allocation fails.</exception>
    /// <exception cref="Kassyi.GSharp.Runtime.Interop.CudaException">Thrown by the default backend when CUDA driver memory allocation fails.</exception>
    public GpuBigInteger(GpuContext context, int numWords)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        NumWords = numWords;

        DevicePointer = Context.Provider.AllocateMemory(numWords);

        if (DevicePointer == nint.Zero)
        {
            throw new OutOfMemoryException(
                $"Failed to allocate GPU memory for {numWords} words ({numWords * 4} bytes).");
        }

        // Zero-clear the newly allocated GPU memory to prevent uninitialized memory bugs
        var zeroArray = new uint[numWords];
        Context.Provider.CopyHostToDevice(DevicePointer, zeroArray, numWords);
    }

    /// <summary>
    /// Allocates GPU device memory and initializes it with the provided host array in little-endian limb order.
    /// </summary>
    /// <param name="context">The parent GPU execution context.</param>
    /// <param name="hostData">The host data array containing 32-bit limbs in little-endian order.</param>
    public GpuBigInteger(GpuContext context, uint[] hostData) : this(context, hostData.Length)
    {
        Context.Provider.CopyHostToDevice(DevicePointer, hostData, NumWords);
    }

    /// <summary>
    /// Evaluates the underlying GPU expression tree into the target buffer.
    /// </summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="targetResult">Optional destination buffer. If <see langword="null"/>, returns this instance.</param>
    /// <returns>The evaluated <see cref="GpuBigInteger"/> result buffer.</returns>
    public GpuBigInteger Evaluate(GpuContext context, GpuBigInteger? targetResult = null)
    {
        if (targetResult is null || ReferenceEquals(targetResult, this))
        {
            return this;
        }

        targetResult.CopyFrom(this);
        return targetResult;
    }

    /// <summary>
    /// Gets or sets the lazy expression wrapper assigned to this big integer buffer.
    /// </summary>
    public GpuBigIntExpr Value
    {
        get => new(this);
        set => value.Node.Evaluate(Context, this);
    }

    /// <summary>Adds two big integers using lazy expression evaluation.</summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns>A deferred expression representing the addition.</returns>
    public static GpuBigIntExpr operator +(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new AddExpression(leftOperand, rightOperand));

    /// <summary>Subtracts one big integer from another using lazy expression evaluation.</summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns>A deferred expression representing the subtraction.</returns>
    public static GpuBigIntExpr operator -(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new SubExpression(leftOperand, rightOperand));

    /// <summary>Multiplies two big integers using lazy expression evaluation.</summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns>A deferred expression representing the multiplication.</returns>
    public static GpuBigIntExpr operator *(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new MulExpression(leftOperand, rightOperand));

    /// <summary>Divides one big integer by another using lazy expression evaluation.</summary>
    /// <param name="leftOperand">The dividend.</param>
    /// <param name="rightOperand">The divisor.</param>
    /// <returns>A deferred expression representing the division.</returns>
    public static GpuBigIntExpr operator /(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new DivExpression(leftOperand, rightOperand));

    /// <summary>Computes the remainder of dividing one big integer by another using lazy expression evaluation.</summary>
    /// <param name="leftOperand">The dividend.</param>
    /// <param name="rightOperand">The divisor.</param>
    /// <returns>A deferred expression representing the modulo.</returns>
    public static GpuBigIntExpr operator %(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new ModExpression(leftOperand, rightOperand));

    /// <summary>Performs bitwise AND on two big integers using lazy expression evaluation.</summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns>A deferred expression representing the bitwise AND.</returns>
    public static GpuBigIntExpr operator &(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new BitwiseAndExpression(leftOperand, rightOperand));

    /// <summary>Shifts a big integer left by a specified number of bits using lazy expression evaluation.</summary>
    /// <param name="leftOperand">The value to shift.</param>
    /// <param name="shiftBits">The number of bits to shift.</param>
    /// <returns>A deferred expression representing the left shift.</returns>
    public static GpuBigIntExpr operator <<(GpuBigInteger leftOperand, int shiftBits) =>
        new(new ShiftLeftExpression(leftOperand, shiftBits));

    /// <summary>Shifts a big integer right by a specified number of bits using lazy expression evaluation.</summary>
    /// <param name="leftOperand">The value to shift.</param>
    /// <param name="shiftBits">The number of bits to shift.</param>
    /// <returns>A deferred expression representing the right shift.</returns>
    public static GpuBigIntExpr operator >>(GpuBigInteger leftOperand, int shiftBits) =>
        new(new ShiftRightExpression(leftOperand, shiftBits));

    /// <summary>Performs bitwise OR on two big integers using lazy expression evaluation.</summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns>A deferred expression representing the bitwise OR.</returns>
    public static GpuBigIntExpr operator |(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new BitwiseOrExpression(leftOperand, rightOperand));

    /// <summary>Performs bitwise exclusive OR on two big integers using lazy expression evaluation.</summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns>A deferred expression representing the bitwise XOR.</returns>
    public static GpuBigIntExpr operator ^(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new BitwiseXorExpression(leftOperand, rightOperand));

    /// <summary>Performs bitwise NOT on a big integer using lazy expression evaluation.</summary>
    /// <param name="leftOperand">The operand.</param>
    /// <returns>A deferred expression representing the bitwise NOT.</returns>
    public static GpuBigIntExpr operator ~(GpuBigInteger leftOperand) =>
        new(new BitwiseNotExpression(leftOperand));

    /// <summary>Determines whether two big integers represent the same value on the GPU.</summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns><c>true</c> if equal; otherwise, <c>false</c>.</returns>
    /// <remarks>This evaluates synchronously by triggering a GPU device memory comparison.</remarks>
    public static bool operator ==(GpuBigInteger? leftOperand, GpuBigInteger? rightOperand)
    {
        if (ReferenceEquals(leftOperand, rightOperand))
        {
            return true;
        }

        if (leftOperand is null || rightOperand is null)
        {
            return false;
        }

        return leftOperand.Context.Provider.CompareTo(leftOperand.Context, leftOperand, rightOperand) == 0;
    }

    /// <summary>Determines whether two big integers represent different values on the GPU.</summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns><c>true</c> if not equal; otherwise, <c>false</c>.</returns>
    /// <remarks>This evaluates synchronously by triggering a GPU device memory comparison.</remarks>
    public static bool operator !=(GpuBigInteger? leftOperand, GpuBigInteger? rightOperand) =>
        !(leftOperand == rightOperand);

    /// <summary>Determines whether the first big integer is less than the second on the GPU.</summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns><c>true</c> if less; otherwise, <c>false</c>.</returns>
    /// <remarks>This evaluates synchronously by triggering a GPU device memory comparison.</remarks>
    public static bool operator <(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        leftOperand.Context.Provider.CompareTo(leftOperand.Context, leftOperand, rightOperand) < 0;

    /// <summary>Determines whether the first big integer is greater than the second on the GPU.</summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns><c>true</c> if greater; otherwise, <c>false</c>.</returns>
    /// <remarks>This evaluates synchronously by triggering a GPU device memory comparison.</remarks>
    public static bool operator >(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        leftOperand.Context.Provider.CompareTo(leftOperand.Context, leftOperand, rightOperand) > 0;

    /// <summary>Determines whether the first big integer is less than or equal to the second on the GPU.</summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns><c>true</c> if less or equal; otherwise, <c>false</c>.</returns>
    /// <remarks>This evaluates synchronously by triggering a GPU device memory comparison.</remarks>
    public static bool operator <=(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        leftOperand.Context.Provider.CompareTo(leftOperand.Context, leftOperand, rightOperand) <= 0;

    /// <summary>Determines whether the first big integer is greater than or equal to the second on the GPU.</summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns><c>true</c> if greater or equal; otherwise, <c>false</c>.</returns>
    /// <remarks>This evaluates synchronously by triggering a GPU device memory comparison.</remarks>
    public static bool operator >=(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        leftOperand.Context.Provider.CompareTo(leftOperand.Context, leftOperand, rightOperand) >= 0;

    /// <summary>
    /// Copies the limb data from GPU device memory back to a host managed array.
    /// </summary>
    /// <returns>A 32-bit unsigned integer array of length <see cref="NumWords"/> in little-endian limb order.</returns>
    public uint[] ToHostArray()
    {
        var hostData = new uint[NumWords];
        Context.Provider.CopyDeviceToHost(hostData, DevicePointer, NumWords);

        return hostData;
    }

    /// <summary>
    /// Clears the GPU memory (sets to 0) for the specified range.
    /// </summary>
    /// <param name="offsetWords">The starting word offset.</param>
    /// <param name="numWords">The number of words to clear. If null, clears to the end.</param>
    public void Clear(int offsetWords = 0, int? numWords = null)
    {
        int wordsToClear = numWords ?? (NumWords - offsetWords);
        if (wordsToClear <= 0)
        {
            return;
        }

        var zeroArray = new uint[wordsToClear];
        Context.Provider.CopyHostToDevice(nint.Add(DevicePointer, offsetWords * 4), zeroArray, wordsToClear);
    }

    /// <summary>
    /// Copies words from a source <see cref="GpuBigInteger"/> to this buffer on the GPU device.
    /// </summary>
    /// <param name="source">The source GPU buffer.</param>
    /// <param name="destOffsetWords">The starting word offset in this destination buffer.</param>
    /// <param name="srcOffsetWords">The starting word offset in the source buffer.</param>
    /// <param name="numWords">The number of 32-bit limbs to copy, or <see langword="null"/> to copy maximum available.</param>
    public void CopyFrom(GpuBigInteger source, int destOffsetWords = 0, int srcOffsetWords = 0, int? numWords = null)
    {
        var wordsToCopy = numWords ?? System.Math.Min(NumWords - destOffsetWords, source.NumWords - srcOffsetWords);

        if (wordsToCopy <= 0)
        {
            return;
        }

        var destPtr = nint.Add(DevicePointer, destOffsetWords * 4);
        var srcPtr = nint.Add(source.DevicePointer, srcOffsetWords * 4);

        Context.Provider.CopyDeviceToDevice(destPtr, srcPtr, wordsToCopy);
    }

    /// <summary>
    /// Copies the most significant limbs (top words) from GPU memory to host.
    /// </summary>
    /// <param name="wordCount">The number of top words to retrieve.</param>
    /// <returns>A host array containing the most significant limbs.</returns>
    public uint[] GetTopWords(int wordCount)
    {
        if (wordCount > NumWords)
        {
            wordCount = NumWords;
        }

        var hostData = new uint[wordCount];
        var srcPtr = nint.Add(DevicePointer, (NumWords - wordCount) * 4);
        Context.Provider.CopyDeviceToHost(hostData, srcPtr, wordCount);
        return hostData;
    }

    /// <summary>
    /// Compares this big integer with another <see cref="GpuBigInteger"/> buffer.
    /// </summary>
    /// <param name="other">The target big integer to compare against.</param>
    /// <returns>-1 if this is less than <paramref name="other"/>, 0 if equal, 1 if greater.</returns>
    public int CompareTo(GpuBigInteger other) => Context.Provider.CompareTo(Context, this, other);

    /// <summary>Determines whether this instance and a specified object have equivalent numerical value.</summary>
    public override bool Equals(object? obj)
    {
        if (obj is GpuBigInteger other)
        {
            return CompareTo(other) == 0;
        }

        return false;
    }

    /// <summary>Returns the hash code for this instance based on its GPU device memory address.</summary>
    public override int GetHashCode()
    {
        // Simple hash code just for the interface (warning: evaluating might be expensive)
        return DevicePointer.GetHashCode();
    }

    /// <summary>Returns a diagnostic string describing this GPU big integer buffer.</summary>
    public override string ToString() => $"GpuBigInteger(Words={NumWords}, DevicePointer=0x{DevicePointer:X})";

    /// <summary>Clears the single word at the specified index to zero on the GPU.</summary>
    /// <param name="wordIndex">The zero-based limb index.</param>
    public void ClearWord(int wordIndex)
    {
        var destPtr = nint.Add(DevicePointer, wordIndex * 4);
        Context.Provider.CopyHostToDevice(destPtr, [0], 1);
    }

    /// <summary>
    /// Creates a new <see cref="GpuBigInteger"/> on the GPU representing <c>2^bitIndex</c>.
    /// </summary>
    /// <param name="context">The GPU context.</param>
    /// <param name="numWords">The allocated word count.</param>
    /// <param name="bitIndex">The zero-based bit index to set.</param>
    /// <returns>A new <see cref="GpuBigInteger"/> holding the power of two value.</returns>
    public static GpuBigInteger CreatePowerOfTwo(GpuContext context, int numWords, int bitIndex)
    {
        var hostData = new uint[numWords];
        var wordIndex = bitIndex / 32;
        var bitOffset = bitIndex % 32;
        if (wordIndex < numWords)
        {
            hostData[wordIndex] = 1u << bitOffset;
        }

        return new GpuBigInteger(context, hostData);
    }


    internal void DisposeCore()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (DevicePointer != nint.Zero)
        {
            Context.Provider.FreeMemory(DevicePointer);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the allocated GPU device memory or returns the buffer to the context memory pool if pooled.
    /// </summary>
    public void Dispose()
    {
        if (IsTemp)
        {
            Context.ReturnTemp(this);
            return;
        }

        DisposeCore();
    }

    ~GpuBigInteger()
    {
        DisposeCore();
    }

    /// <summary>Checks whether this big integer equals zero on the GPU.</summary>
    public bool IsZero() => Context.Provider.IsZero(Context, this);

    /// <summary>Checks whether this big integer equals one on the GPU.</summary>
    public bool IsOne() => Context.Provider.IsOne(Context, this);

    /// <summary>Computes the total number of significant bits used to represent this integer on the GPU.</summary>
    public int GetBitLength() => Context.Provider.GetBitLength(Context, this);

    /// <summary>Computes the total number of bytes required to hold this integer.</summary>
    public int GetByteCount()
    {
        var bitLength = GetBitLength();
        return bitLength == 0 ? 1 : (bitLength + 7) / 8;
    }

    /// <summary>Gets a value indicating whether this big integer is an even number.</summary>
    public bool IsEven
    {
        get
        {
            var word = new uint[1];
            Context.Provider.CopyDeviceToHost(word, DevicePointer, 1);
            return (word[0] & 1) == 0;
        }
    }

    /// <summary>Gets or sets the sign bit of this integer (stored in the MSB of the highest word).</summary>
    public bool IsNegative
    {
        get
        {
            var word = new uint[1];
            Context.Provider.CopyDeviceToHost(word, nint.Add(DevicePointer, (NumWords - 1) * 4), 1);
            return (word[0] & 0x80000000) != 0;
        }
        set
        {
            var word = new uint[1];
            var ptr = nint.Add(DevicePointer, (NumWords - 1) * 4);
            Context.Provider.CopyDeviceToHost(word, ptr, 1);
            if (value)
            {
                word[0] |= 0x80000000;
            }
            else
            {
                word[0] &= 0x7FFFFFFF;
            }

            Context.Provider.CopyHostToDevice(ptr, word, 1);
        }
    }

    /// <summary>
    /// Computes self-squaring (<c>A * A</c>) in-place on the GPU.
    /// </summary>
    /// <param name="logicalWords">The logical number of input limbs to square.</param>
    /// <returns><see langword="true"/> if executed successfully; otherwise, <see langword="false"/>.</returns>
    public bool Square(int logicalWords)
    {
        if (NumWords < logicalWords * 2)
        {
            throw new InvalidOperationException(
                $"Square requires the destination buffer to have at least {logicalWords * 2} words, but only has {NumWords}.");
        }

        return Context.Provider.Square(Context, DevicePointer, logicalWords);
    }

    /// <summary>
    /// Creates a new <see cref="GpuBigInteger"/> padded to the specified target words with zeros.
    /// If the target size equals the current size, a duplicate is returned.
    /// </summary>
    /// <param name="targetWords">The requested minimum number of words.</param>
    /// <returns>A new <see cref="GpuBigInteger"/> instance padded to the target size.</returns>
    public GpuBigInteger PadTo(int targetWords)
    {
        var result = new GpuBigInteger(Context, targetWords);
        var copyWords = System.Math.Min(NumWords, targetWords);
        Context.Provider.CopyDeviceToDevice(result.DevicePointer, DevicePointer, copyWords);
        return result;
    }

    /// <summary>
    /// Creates a <see cref="GpuBigInteger"/> by uploading a BCL <see cref="BigInteger"/> to the GPU.
    /// </summary>
    public static GpuBigInteger FromBigInteger(GpuContext context, BigInteger val, int minWords = 0)
    {
        val = BigInteger.Abs(val);
        if (val.IsZero)
        {
            return new GpuBigInteger(context, System.Math.Max(1, minWords));
        }

        var byteCount = val.GetByteCount(isUnsigned: true);
        var words = (byteCount + 3) / 4;
        var allocWords = System.Math.Max(words, minWords);

        var hostData = new uint[allocWords];
        var byteSpan = MemoryMarshal.AsBytes(hostData.AsSpan());
        val.TryWriteBytes(byteSpan, out _, isUnsigned: true, isBigEndian: false);

        return new GpuBigInteger(context, hostData);
    }

    /// <summary>
    /// Downloads the integer from the GPU and converts it to a BCL <see cref="BigInteger"/>.
    /// </summary>
    public BigInteger ToBigInteger(bool isNegative = false)
    {
        var hostData = new uint[NumWords];
        Context.Provider.CopyDeviceToHost(hostData, DevicePointer, NumWords);
        
        var byteSpan = MemoryMarshal.AsBytes(hostData.AsSpan());
        var big = new BigInteger(byteSpan, isUnsigned: true, isBigEndian: false);
        return isNegative ? -big : big;
    }
}



