using System.Buffers;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;
using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

/// <summary>
/// Represents an arbitrary-precision unsigned integer stored in NVIDIA GPU global device memory.
/// </summary>
/// <remarks>
/// <para>All arithmetic and bitwise operations are executed on the GPU using SIMD/warp-level intrinsics.</para>
/// <para><b>Memory Management:</b> Implements <see cref="IDisposable"/> to ensure deterministic GPU memory deallocation.</para>
/// </remarks>
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
internal sealed class GpuUIntBigIntegerBuffer : IDisposable
{
    /// <summary>
    /// Gets the native GPU global device memory pointer (<c>CUdeviceptr</c>) holding the integer limb data.
    /// </summary>
    public IntPtr DevicePointer { get; }

    /// <summary>
    /// Gets the total number of 32-bit words (limbs) allocated for this big integer.
    /// </summary>
    public int NumWords { get; }

    /// <summary>
    /// Gets the parent <see cref="GpuContext"/> associated with this instance.
    /// </summary>
    public GpuContext Context { get; }

    internal bool IsTemp { get; set; }

    internal bool Disposed;

    /// <summary>
    /// Allocates uninitialized GPU device memory for an integer of the specified limb length.
    /// </summary>
    /// <param name="context">The parent GPU execution context.</param>
    /// <param name="numWords">The number of 32-bit limbs to allocate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="OutOfMemoryException">Thrown when device memory allocation fails.</exception>
    /// <exception cref="Kassyi.GSharp.Runtime.Interop.CudaException">Thrown by the default backend when CUDA driver memory allocation fails.</exception>
    public GpuUIntBigIntegerBuffer(GpuContext context, int numWords)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        NumWords = numWords;

        DevicePointer = Context.Provider.AllocateMemory(numWords);

        InitializeMemory();
    }

    /// <summary>
    /// Initializes an instance with a pre-allocated raw device pointer (Internal Transition Phase).
    /// </summary>
    internal GpuUIntBigIntegerBuffer(GpuContext context, int numWords, IntPtr preAllocatedPointer, bool clearMemory = false)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        NumWords = numWords;
        DevicePointer = preAllocatedPointer;

        if (clearMemory)
        {
            InitializeMemory();
        }
    }

    private void InitializeMemory()
    {
        if (DevicePointer == IntPtr.Zero)
        {
            throw new OutOfMemoryException(
                $"Failed to allocate GPU memory for {NumWords} words ({NumWords * 4} bytes).");
        }

        // Zero-clear the newly allocated GPU memory to prevent uninitialized memory bugs
        var zeroArray = ArrayPool<uint>.Shared.Rent(NumWords);
        try
        {
            Array.Clear(zeroArray, 0, NumWords);
            Context.Provider.CopyHostToDevice(DevicePointer, zeroArray, NumWords, Context.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(zeroArray);
        }
    }

    /// <summary>
    /// Allocates GPU device memory and initializes it with the provided host array in little-endian limb order.
    /// </summary>
    /// <param name="context">The parent GPU execution context.</param>
    /// <param name="hostData">The host data array containing 32-bit limbs in little-endian order.</param>
    public GpuUIntBigIntegerBuffer(GpuContext context, uint[] hostData) : this(context, hostData.Length)
    {
        Context.Provider.CopyHostToDevice(DevicePointer, hostData, NumWords, Context.Stream);
    }

    /// <summary>
    /// <returns>A 32-bit unsigned integer array of length <see cref="NumWords"/> in little-endian limb order.</returns>
    public uint[] ToHostArray()
    {
        var hostData = new uint[NumWords];
        Context.Provider.CopyDeviceToHost(hostData, DevicePointer, NumWords, Context.Stream);

        return hostData;
    }

    /// <summary>
    /// Clears the GPU memory (sets to 0) for the specified range.
    /// </summary>
    /// <param name="offsetWords">The starting word offset.</param>
    /// <param name="numWords">The number of words to clear. If null, clears to the end.</param>
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
            Context.Provider.CopyHostToDevice(IntPtr.Add(DevicePointer, offsetWords * 4), zeroArray, wordsToClear,
                Context.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(zeroArray);
        }
    }

    /// <summary>
    /// Copies words from a source <see cref="GpuUIntBigIntegerBuffer"/> to this buffer on the GPU device.
    /// </summary>
    /// <param name="source">The source GPU buffer.</param>
    /// <param name="destOffsetWords">The starting word offset in this destination buffer.</param>
    /// <param name="srcOffsetWords">The starting word offset in the source buffer.</param>
    /// <param name="numWords">The number of 32-bit limbs to copy, or <see langword="null"/> to copy maximum available.</param>
    public void CopyFrom(GpuUIntBigIntegerBuffer source, int destOffsetWords = 0, int srcOffsetWords = 0,
        int? numWords = null)
    {
        var wordsToCopy = numWords ?? System.Math.Min(NumWords - destOffsetWords, source.NumWords - srcOffsetWords);

        if (wordsToCopy <= 0)
        {
            return;
        }

        var destPtr = IntPtr.Add(DevicePointer, destOffsetWords * 4);
        var srcPtr = IntPtr.Add(source.DevicePointer, srcOffsetWords * 4);

        Context.Provider.CopyDeviceToDevice(destPtr, srcPtr, wordsToCopy, Context.Stream);
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
        var srcPtr = IntPtr.Add(DevicePointer, (NumWords - wordCount) * 4);
        Context.Provider.CopyDeviceToHost(hostData, srcPtr, wordCount, Context.Stream);
        return hostData;
    }

    /// <summary>
    /// Compares this big integer with another <see cref="GpuUIntBigIntegerBuffer"/> buffer.
    /// </summary>
    /// <param name="other">The target big integer to compare against.</param>
    /// <returns>-1 if this is less than <paramref name="other"/>, 0 if equal, 1 if greater.</returns>
    public int CompareTo(GpuUIntBigIntegerBuffer other) => Context.Provider.CompareTo(Context, new GpuUIntBigInteger(this), new GpuUIntBigInteger(other));

    /// <summary>Determines whether this instance and a specified object have equivalent numerical value.</summary>
    public override bool Equals(object? obj)
    {
        if (obj is GpuUIntBigIntegerBuffer other)
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
    public override string ToString() => $"GpuUIntBigIntegerBuffer(Words={NumWords}, DevicePointer=0x{DevicePointer:X})";

    /// <summary>Clears the single word at the specified index to zero on the GPU.</summary>
    /// <param name="wordIndex">The zero-based limb index.</param>
    public void ClearWord(int wordIndex)
    {
        var destPtr = IntPtr.Add(DevicePointer, wordIndex * 4);
        Context.Provider.CopyHostToDevice(destPtr, [0], 1, Context.Stream);
    }

    /// <summary>
    /// Creates a new <see cref="GpuUIntBigIntegerBuffer"/> on the GPU representing <c>2^bitIndex</c>.
    /// </summary>
    /// <param name="context">The GPU context.</param>
    /// <param name="numWords">The allocated word count.</param>
    /// <param name="bitIndex">The zero-based bit index to set.</param>
    /// <returns>A new <see cref="GpuUIntBigIntegerBuffer"/> holding the power of two value.</returns>
    public static GpuUIntBigIntegerBuffer CreatePowerOfTwo(GpuContext context, int numWords, int bitIndex)
    {
        var result = new GpuUIntBigIntegerBuffer(context, numWords);
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
            context.Provider.CopyHostToDevice(IntPtr.Add(result.DevicePointer, wordIndex * 4), word, 1, context.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(word);
        }

        return result;
    }


    internal void DisposeCore()
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;

        if (DevicePointer != IntPtr.Zero && !IsTemp)
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
        if (Disposed)
        {
            return;
        }

        if (IsTemp)
        {
            Context.ReturnTemp(new GpuUIntBigInteger(this));
        }

        DisposeCore();
    }

    ~GpuUIntBigIntegerBuffer()
    {
        DisposeCore();
    }

    /// <summary>Checks whether this big integer equals zero on the GPU.</summary>
    public bool IsZero() => Context.Provider.IsZero(Context, new GpuUIntBigInteger(this));

    /// <summary>Checks whether this big integer equals one on the GPU.</summary>
    public bool IsOne() => Context.Provider.IsOne(Context, new GpuUIntBigInteger(this));

    /// <summary>Computes the total number of significant bits used to represent this integer on the GPU.</summary>
    public int GetBitLength() => Context.Provider.GetBitLength(Context, new GpuUIntBigInteger(this));

    /// <summary>Computes the total number of bytes required to hold this integer.</summary>
    public int GetByteCount()
    {
        var bitLength = GetBitLength();
        return bitLength == 0 ? 1 : (bitLength + 7) / 8;
    }

    /// <summary>Gets a value indicating whether this big integer is an even number.</summary>
    public bool GetIsEven()
    {
        var word = Context.Provider.ReadWord(DevicePointer);
        return (word & 1) == 0;
    }

    /// <summary>Gets the sign bit of this integer (stored in the MSB of the highest word).</summary>
    public bool GetIsNegative()
    {
        var word = Context.Provider.ReadWord(DevicePointer, NumWords - 1);
        return (word & 0x80000000) != 0;
    }

    /// <summary>Sets the sign bit of this integer (stored in the MSB of the highest word).</summary>
    public void SetIsNegative(bool value)
    {
        var word = Context.Provider.ReadWord(DevicePointer, NumWords - 1);
        if (value)
        {
            word |= 0x80000000;
        }
        else
        {
            word &= 0x7FFFFFFF;
        }

        Context.Provider.WriteWord(DevicePointer, NumWords - 1, word);
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
    /// Creates a new <see cref="GpuUIntBigIntegerBuffer"/> padded to the specified target words with zeros.
    /// If the target size equals the current size, a duplicate is returned.
    /// </summary>
    /// <param name="targetWords">The requested minimum number of words.</param>
    /// <returns>A new <see cref="GpuUIntBigIntegerBuffer"/> instance padded to the target size.</returns>
    public GpuUIntBigIntegerBuffer PadTo(int targetWords)
    {
        var result = new GpuUIntBigIntegerBuffer(Context, targetWords);
        var copyWords = System.Math.Min(NumWords, targetWords);
        Context.Provider.CopyDeviceToDevice(result.DevicePointer, DevicePointer, copyWords, Context.Stream);
        return result;
    }

    /// <summary>
    /// Creates a <see cref="GpuUIntBigIntegerBuffer"/> by uploading a BCL <see cref="BigInteger"/> to the GPU.
    /// </summary>
    public static GpuUIntBigIntegerBuffer FromBigInteger(GpuContext context, BigInteger val, int minWords = 0)
    {
        val = BigInteger.Abs(val);
        if (val.IsZero)
        {
            return new GpuUIntBigIntegerBuffer(context, System.Math.Max(1, minWords));
        }

        var byteCount = val.GetByteCount(isUnsigned: true);
        var words = (byteCount + 3) / 4;
        var allocWords = System.Math.Max(words, minWords);

        var hostData = ArrayPool<uint>.Shared.Rent(allocWords);
        try
        {
            hostData.AsSpan(0, allocWords).Clear();
            var byteSpan = MemoryMarshal.AsBytes(hostData.AsSpan());
            val.TryWriteBytes(byteSpan, out _, isUnsigned: true, isBigEndian: false);

            var result = new GpuUIntBigIntegerBuffer(context, allocWords);
            context.Provider.CopyHostToDevice(result.DevicePointer, hostData, allocWords, context.Stream);
            return result;
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostData);
        }
    }

    /// <summary>
    /// Downloads the integer from the GPU and converts it to a BCL <see cref="BigInteger"/>.
    /// </summary>
    public BigInteger ToBigInteger(bool isNegative = false)
    {
        var hostData = ArrayPool<uint>.Shared.Rent(NumWords);
        try
        {
            Context.Provider.CopyDeviceToHost(hostData, DevicePointer, NumWords, Context.Stream);

            var byteSpan = MemoryMarshal.AsBytes(hostData.AsSpan(0, NumWords));
            var big = new BigInteger(byteSpan, isUnsigned: true, isBigEndian: false);
            return isNegative ? -big : big;
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostData);
        }
    }




}
