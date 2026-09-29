using System.Buffers;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;
using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Represents an arbitrary-precision unsigned integer stored in NVIDIA GPU global device memory.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct GpuUIntBigInteger : IDisposable, IGpuBigUIntExpression, IEquatable<GpuUIntBigInteger>,
    IComparable<GpuUIntBigInteger>
{
    public GpuWorkspace Workspace { get; }
    public BigIntMemoryHandle Handle { get; }

    public IntPtr DevicePointer =>
        Handle.IsDefault ? IntPtr.Zero : IntPtr.Add(Handle.Buffer.Pointer, Handle.WordOffset * 4);

    public int NumWords => Handle.IsDefault ? 0 : Handle.NumWords;
    public bool IsTemp { get; }

    internal GpuUIntBigInteger(GpuWorkspace workspace, BigIntMemoryHandle handle, bool isTemp = false)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Handle = handle;
        IsTemp = isTemp;
        if (!isTemp)
        {
            workspace.Track(this);
        }
    }

    internal GpuUIntBigInteger(GpuWorkspace workspace, int numWords, IntPtr preAllocatedPointer, bool isTemp = false)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        var buffer = new BigIntDeviceBuffer(preAllocatedPointer, numWords);
        Handle = new BigIntMemoryHandle(buffer, 0, numWords);
        IsTemp = isTemp;
        if (!isTemp)
        {
            workspace.Track(this);
        }
    }

    public GpuUIntBigInteger(GpuWorkspace workspace, int numWords)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Handle = Workspace.MemoryManager.Allocate(numWords);
        IsTemp = false;
        workspace.Track(this);
    }

    public GpuUIntBigInteger(GpuWorkspace workspace, uint[] hostData)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Handle = Workspace.MemoryManager.Allocate(hostData.Length);
        IsTemp = false;
        Workspace.Provider.CopyHostToDevice(DevicePointer, hostData, NumWords, Workspace.Stream);
        workspace.Track(this);
    }

    public GpuBigUIntExpr Value
    {
        get => new(this);
        set => value.Node.Evaluate(Workspace, this);
    }

    public static GpuBigUIntExpr operator +(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        new(new AddExpression(leftOperand, rightOperand));

    public static GpuBigUIntExpr operator -(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        new(new SubExpression(leftOperand, rightOperand));

    public static GpuBigUIntExpr operator *(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        new(new MulExpression(leftOperand, rightOperand));

    public static GpuBigUIntExpr operator /(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        new(new DivExpression(leftOperand, rightOperand));

    public static GpuBigUIntExpr operator %(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        new(new ModExpression(leftOperand, rightOperand));

    public static GpuBigUIntExpr operator &(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        new(new BitwiseAndExpression(leftOperand, rightOperand));

    public static GpuBigUIntExpr operator <<(GpuUIntBigInteger leftOperand, int shiftBits) =>
        new(new ShiftLeftExpression(leftOperand, shiftBits));

    public static GpuBigUIntExpr operator >> (GpuUIntBigInteger leftOperand, int shiftBits) =>
        new(new ShiftRightExpression(leftOperand, shiftBits));

    public static GpuBigUIntExpr operator |(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        new(new BitwiseOrExpression(leftOperand, rightOperand));

    public static GpuBigUIntExpr operator ^(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        new(new BitwiseXorExpression(leftOperand, rightOperand));

    public static GpuBigUIntExpr operator ~(GpuUIntBigInteger leftOperand) =>
        new(new BitwiseNotExpression(leftOperand));

    public static bool operator ==(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        leftOperand.Equals(rightOperand);

    public static bool operator !=(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        !(leftOperand == rightOperand);

    public static bool operator <(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        leftOperand.CompareTo(rightOperand) < 0;

    public static bool operator >(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        leftOperand.CompareTo(rightOperand) > 0;

    public static bool operator <=(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        leftOperand.CompareTo(rightOperand) <= 0;

    public static bool operator >=(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        leftOperand.CompareTo(rightOperand) >= 0;

    public uint[] ToHostArray()
    {
        var hostData = new uint[NumWords];
        Workspace.Provider.CopyDeviceToHost(hostData, DevicePointer, NumWords, Workspace.Stream);
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
            Workspace.Provider.CopyHostToDevice(IntPtr.Add(DevicePointer, offsetWords * 4), zeroArray, wordsToClear,
                Workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(zeroArray);
        }
    }

    public void CopyFrom(GpuUIntBigInteger source, int destOffsetWords = 0, int srcOffsetWords = 0,
        int? numWords = null)
    {
        var wordsToCopy = numWords ?? System.Math.Min(NumWords - destOffsetWords, source.NumWords - srcOffsetWords);
        if (wordsToCopy <= 0)
        {
            return;
        }

        var destPtr = IntPtr.Add(DevicePointer, destOffsetWords * 4);
        var srcPtr = IntPtr.Add(source.DevicePointer, srcOffsetWords * 4);
        Workspace.Provider.CopyDeviceToDevice(destPtr, srcPtr, wordsToCopy, Workspace.Stream);
    }

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

    public int CompareTo(GpuUIntBigInteger other) => Workspace.Provider.CompareTo(Workspace, this, other);
    public override bool Equals(object? obj) => obj is GpuUIntBigInteger other && Equals(other);

    public bool Equals(GpuUIntBigInteger other) =>
        Handle.Equals(other.Handle) || (Workspace != null && CompareTo(other) == 0);

    public override int GetHashCode() => Handle.GetHashCode();
    public override string ToString() => "GpuUIntBigInteger(Words={NumWords}, DevicePointer=0x{DevicePointer:X})";

    public void ClearWord(int wordIndex)
    {
        var destPtr = IntPtr.Add(DevicePointer, wordIndex * 4);
        Workspace.Provider.CopyHostToDevice(destPtr, [0], 1, Workspace.Stream);
    }

    public static GpuUIntBigInteger CreatePowerOfTwo(GpuWorkspace context, int numWords, int bitIndex)
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
        if (Workspace != null && !Handle.IsDefault && !IsTemp)
        {
            Workspace.MemoryManager.Free(Handle);
        }
    }

    public bool IsZero() => Workspace.Provider.IsZero(Workspace, this);
    public bool IsOne() => Workspace.Provider.IsOne(Workspace, this);
    public int GetBitLength() => Workspace.Provider.GetBitLength(Workspace, this);

    public int GetByteCount()
    {
        var bitLength = GetBitLength();
        return bitLength == 0 ? 1 : (bitLength + 7) / 8;
    }

    public bool GetIsEven()
    {
        var word = Workspace.Provider.ReadWord(DevicePointer);
        return (word & 1) == 0;
    }

    public bool GetIsNegative()
    {
        var word = Workspace.Provider.ReadWord(DevicePointer, NumWords - 1);
        return (word & 0x80000000) != 0;
    }

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

    public bool Square(int logicalWords)
    {
        if (NumWords < logicalWords * 2)
        {
            throw new InvalidOperationException(
                $"Square requires the destination buffer to have at least {logicalWords * 2} words, but only has {NumWords}.");
        }

        return Workspace.Provider.Square(Workspace, DevicePointer, logicalWords);
    }

    public GpuUIntBigInteger PadTo(int targetWords)
    {
        var result = new GpuUIntBigInteger(Workspace, targetWords);
        var copyWords = System.Math.Min(NumWords, targetWords);
        Workspace.Provider.CopyDeviceToDevice(result.DevicePointer, DevicePointer, copyWords, Workspace.Stream);
        return result;
    }

    public static GpuUIntBigInteger FromBigInteger(GpuWorkspace context, BigInteger val, int minWords = 0)
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

    public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) =>
        visitor.VisitGpuUIntBigInteger(this);
}
