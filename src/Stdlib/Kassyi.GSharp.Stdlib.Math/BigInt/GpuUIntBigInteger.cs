using System.Buffers;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;
using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Represents an arbitrary-precision unsigned integer stored in NVIDIA GPU global device memory.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct GpuUIntBigInteger : IDisposable, IGpuBigUIntExpression, IEquatable<GpuUIntBigInteger>, IComparable<GpuUIntBigInteger>
{
    public GpuContext Context { get; }
    public BigIntMemoryHandle Handle { get; }

    public IntPtr DevicePointer => Handle.IsDefault ? IntPtr.Zero : IntPtr.Add(Handle.Buffer.Pointer, Handle.WordOffset * 4);
    public int NumWords => Handle.IsDefault ? 0 : Handle.NumWords;
    public bool IsTemp { get; }

    internal GpuUIntBigInteger(GpuContext context, BigIntMemoryHandle handle, bool isTemp = false)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Handle = handle;
        IsTemp = isTemp;
    }

    internal GpuUIntBigInteger(GpuContext context, int numWords, IntPtr preAllocatedPointer, bool isTemp = false)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        var buffer = new BigIntDeviceBuffer(preAllocatedPointer, numWords);
        Handle = new BigIntMemoryHandle(buffer, 0, numWords);
        IsTemp = isTemp;
    }

    public GpuUIntBigInteger(GpuContext context, int numWords)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Handle = Context.MemoryManager.Allocate(numWords);
        IsTemp = false;
    }

    public GpuUIntBigInteger(GpuContext context, uint[] hostData)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Handle = Context.MemoryManager.Allocate(hostData.Length);
        IsTemp = false;
        Context.Provider.CopyHostToDevice(DevicePointer, hostData, NumWords, Context.Stream);
    }

    public GpuBigUIntExpr Value
    {
        get => new(this);
        set => value.Node.Evaluate(Context, this);
    }

    public static GpuBigUIntExpr operator +(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => new(new AddExpression(leftOperand, rightOperand));
    public static GpuBigUIntExpr operator -(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => new(new SubExpression(leftOperand, rightOperand));
    public static GpuBigUIntExpr operator *(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => new(new MulExpression(leftOperand, rightOperand));
    public static GpuBigUIntExpr operator /(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => new(new DivExpression(leftOperand, rightOperand));
    public static GpuBigUIntExpr operator %(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => new(new ModExpression(leftOperand, rightOperand));
    public static GpuBigUIntExpr operator &(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => new(new BitwiseAndExpression(leftOperand, rightOperand));
    public static GpuBigUIntExpr operator <<(GpuUIntBigInteger leftOperand, int shiftBits) => new(new ShiftLeftExpression(leftOperand, shiftBits));
    public static GpuBigUIntExpr operator >>(GpuUIntBigInteger leftOperand, int shiftBits) => new(new ShiftRightExpression(leftOperand, shiftBits));
    public static GpuBigUIntExpr operator |(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => new(new BitwiseOrExpression(leftOperand, rightOperand));
    public static GpuBigUIntExpr operator ^(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => new(new BitwiseXorExpression(leftOperand, rightOperand));
    public static GpuBigUIntExpr operator ~(GpuUIntBigInteger leftOperand) => new(new BitwiseNotExpression(leftOperand));
    public static bool operator ==(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => leftOperand.Equals(rightOperand);
    public static bool operator !=(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => !(leftOperand == rightOperand);
    public static bool operator <(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => leftOperand.CompareTo(rightOperand) < 0;
    public static bool operator >(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => leftOperand.CompareTo(rightOperand) > 0;
    public static bool operator <=(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => leftOperand.CompareTo(rightOperand) <= 0;
    public static bool operator >=(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) => leftOperand.CompareTo(rightOperand) >= 0;

    public uint[] ToHostArray()
    {
        var hostData = new uint[NumWords];
        Context.Provider.CopyDeviceToHost(hostData, DevicePointer, NumWords, Context.Stream);
        return hostData;
    }

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
            Context.Provider.CopyHostToDevice(IntPtr.Add(DevicePointer, offsetWords * 4), zeroArray, wordsToClear, Context.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(zeroArray);
        }
    }

    public void CopyFrom(GpuUIntBigInteger source, int destOffsetWords = 0, int srcOffsetWords = 0, int? numWords = null)
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

    public int CompareTo(GpuUIntBigInteger other) => Context.Provider.CompareTo(Context, this, other);
    public override bool Equals(object? obj) => obj is GpuUIntBigInteger other && Equals(other);
    public bool Equals(GpuUIntBigInteger other) => Handle.Equals(other.Handle) || (Context != null && CompareTo(other) == 0);
    public override int GetHashCode() => Handle.GetHashCode();
    public override string ToString() => "GpuUIntBigInteger(Words={NumWords}, DevicePointer=0x{DevicePointer:X})";

    public void ClearWord(int wordIndex)
    {
        var destPtr = IntPtr.Add(DevicePointer, wordIndex * 4);
        Context.Provider.CopyHostToDevice(destPtr, [0], 1, Context.Stream);
    }

    public static GpuUIntBigInteger CreatePowerOfTwo(GpuContext context, int numWords, int bitIndex)
    {
        var result = new GpuUIntBigInteger(context, numWords);
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

    public void Dispose()
    {
        if (Context != null && !Handle.IsDefault && !IsTemp)
        {
            Context.MemoryManager.Free(Handle);
        }
    }

    public bool IsZero() => Context.Provider.IsZero(Context, this);
    public bool IsOne() => Context.Provider.IsOne(Context, this);
    public int GetBitLength() => Context.Provider.GetBitLength(Context, this);
    public int GetByteCount()
    {
        var bitLength = GetBitLength();
        return bitLength == 0 ? 1 : (bitLength + 7) / 8;
    }
    public bool GetIsEven()
    {
        var word = Context.Provider.ReadWord(DevicePointer);
        return (word & 1) == 0;
    }
    public bool GetIsNegative()
    {
        var word = Context.Provider.ReadWord(DevicePointer, NumWords - 1);
        return (word & 0x80000000) != 0;
    }
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
    public bool Square(int logicalWords)
    {
        if (NumWords < logicalWords * 2)
        {
            throw new InvalidOperationException($"Square requires the destination buffer to have at least {logicalWords * 2} words, but only has {NumWords}.");
        }

        return Context.Provider.Square(Context, DevicePointer, logicalWords);
    }
    
    public GpuUIntBigInteger PadTo(int targetWords)
    {
        var result = new GpuUIntBigInteger(Context, targetWords);
        var copyWords = System.Math.Min(NumWords, targetWords);
        Context.Provider.CopyDeviceToDevice(result.DevicePointer, DevicePointer, copyWords, Context.Stream);
        return result;
    }

    public static GpuUIntBigInteger FromBigInteger(GpuContext context, BigInteger val, int minWords = 0)
    {
        val = BigInteger.Abs(val);
        if (val.IsZero)
        {
            return new GpuUIntBigInteger(context, System.Math.Max(1, minWords));
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
            var result = new GpuUIntBigInteger(context, allocWords);
            context.Provider.CopyHostToDevice(result.DevicePointer, hostData, allocWords, context.Stream);
            return result;
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostData);
        }
    }

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

    public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) => visitor.VisitGpuUIntBigInteger(this);
}
