using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;
using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

namespace Kassyi.GSharp.Stdlib.Math.BigInt;

/// <summary>
/// Represents an arbitrary-precision unsigned integer stored in NVIDIA GPU global device memory.
/// </summary>
public readonly struct GpuUIntBigInteger : IDisposable, IGpuBigUIntExpression, IEquatable<GpuUIntBigInteger>, IComparable<GpuUIntBigInteger>
{
    internal readonly GpuUIntBigIntegerBuffer Buffer;

    public IntPtr DevicePointer => Buffer.DevicePointer;
    public int NumWords => Buffer.NumWords;
    internal GpuUIntBigInteger(GpuContext context, int numWords, IntPtr preAllocatedPointer) => Buffer = new GpuUIntBigIntegerBuffer(context, numWords, preAllocatedPointer);

    public bool IsTemp => Buffer.IsTemp;
    internal void SetIsTemp(bool value) => Buffer.IsTemp = value;
    public GpuContext Context => Buffer.Context;

    internal GpuUIntBigInteger(GpuUIntBigIntegerBuffer buffer) => Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

    public GpuUIntBigInteger(GpuContext context, int numWords)
    {
        Buffer = new GpuUIntBigIntegerBuffer(context, numWords);
    }

    public GpuUIntBigInteger(GpuContext context, uint[] hostData)
    {
        Buffer = new GpuUIntBigIntegerBuffer(context, hostData);
    }

    public GpuBigUIntExpr Value
    {
        get => new(this);
        set => value.Node.Evaluate(Buffer.Context, this);
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

    public static GpuBigUIntExpr operator >>(GpuUIntBigInteger leftOperand, int shiftBits) =>
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

    public uint[] ToHostArray() => Buffer.ToHostArray();

    public void Clear(int offsetWords = 0, int? numWords = null) => Buffer.Clear(offsetWords, numWords);

    public void CopyFrom(GpuUIntBigInteger source, int destOffsetWords = 0, int srcOffsetWords = 0, int? numWords = null) =>
        Buffer.CopyFrom(source.Buffer, destOffsetWords, srcOffsetWords, numWords);

    public uint[] GetTopWords(int wordCount) => Buffer.GetTopWords(wordCount);

    public int CompareTo(GpuUIntBigInteger other) => Buffer.CompareTo(other.Buffer);

    public override bool Equals(object? obj) => obj is GpuUIntBigInteger other && Equals(other);

    public bool Equals(GpuUIntBigInteger other) => ReferenceEquals(Buffer, other.Buffer) || (Buffer != null && other.Buffer != null && CompareTo(other) == 0);

    public override int GetHashCode() => Buffer?.GetHashCode() ?? 0;

    public override string ToString() => Buffer?.ToString() ?? "GpuUIntBigInteger(null)";

    public void ClearWord(int wordIndex) => Buffer.ClearWord(wordIndex);

    public static GpuUIntBigInteger CreatePowerOfTwo(GpuContext context, int numWords, int bitIndex) =>
        new(GpuUIntBigIntegerBuffer.CreatePowerOfTwo(context, numWords, bitIndex));

    public void Dispose() => Buffer?.Dispose();

    public bool IsZero() => Buffer.IsZero();
    public bool IsOne() => Buffer.IsOne();
    public int GetBitLength() => Buffer.GetBitLength();
    public int GetByteCount() => Buffer.GetByteCount();
    public bool GetIsEven() => Buffer.GetIsEven();
    public bool GetIsNegative() => Buffer.GetIsNegative();
    public void SetIsNegative(bool value) => Buffer.SetIsNegative(value);
    public bool Square(int logicalWords) => Buffer.Square(logicalWords);
    
    public GpuUIntBigInteger PadTo(int targetWords) => new(Buffer.PadTo(targetWords));

    public static GpuUIntBigInteger FromBigInteger(GpuContext context, BigInteger val, int minWords = 0) =>
        new(GpuUIntBigIntegerBuffer.FromBigInteger(context, val, minWords));

    public BigInteger ToBigInteger(bool isNegative = false) => Buffer.ToBigInteger(isNegative);

    public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor) =>
        visitor.VisitGpuUIntBigInteger(this);
}