
using System.Numerics;
using Kassyi.GSharp.Stdlib.Math.Expressions;

namespace Kassyi.GSharp.Stdlib.Math;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
public class GpuBigInteger : IDisposable, IGpuBigIntExpression
{
    public IntPtr DevicePointer { get; private set; }
    public int NumWords { get; }

    public GpuContext Context { get; }

    internal bool IsTemp { get; set; }

    private bool _disposed;

    public GpuBigInteger(GpuContext context, int numWords)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        NumWords = numWords;

        DevicePointer = Context.Provider.AllocateMemory(numWords);

        if (DevicePointer == IntPtr.Zero)
        {
            throw new OutOfMemoryException(
                $"Failed to allocate GPU memory for {numWords} words ({numWords * 4} bytes).");
        }
    }

    public GpuBigInteger(GpuContext context, uint[] hostData) : this(context, hostData.Length)
    {
        Context.Provider.CopyHostToDevice(DevicePointer, hostData, NumWords);
    }

    public GpuBigInteger Evaluate(GpuContext context, GpuBigInteger? targetResult = null)
    {
        if (targetResult is null || ReferenceEquals(targetResult, this))
        {
            return this;
        }

        targetResult.CopyFrom(this);
        return targetResult;
    }

    public GpuBigIntExpr Value
    {
        get => new(this);
        set => value.Node.Evaluate(Context, this);
    }

    public static GpuBigIntExpr operator +(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new AddExpression(leftOperand, rightOperand));

    public static GpuBigIntExpr operator -(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new SubExpression(leftOperand, rightOperand));

    public static GpuBigIntExpr operator *(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new MulExpression(leftOperand, rightOperand));

    public static GpuBigIntExpr operator /(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new DivExpression(leftOperand, rightOperand));

    public static GpuBigIntExpr operator %(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new ModExpression(leftOperand, rightOperand));

    public static GpuBigIntExpr operator &(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new BitwiseAndExpression(leftOperand, rightOperand));

    public static GpuBigIntExpr operator <<(GpuBigInteger leftOperand, int shiftBits) =>
        new(new ShiftLeftExpression(leftOperand, shiftBits));

    public static GpuBigIntExpr operator >> (GpuBigInteger leftOperand, int shiftBits) =>
        new(new ShiftRightExpression(leftOperand, shiftBits));

    public static GpuBigIntExpr operator |(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new BitwiseOrExpression(leftOperand, rightOperand));

    public static GpuBigIntExpr operator ^(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        new(new BitwiseXorExpression(leftOperand, rightOperand));

    public static GpuBigIntExpr operator ~(GpuBigInteger leftOperand) =>
        new(new BitwiseNotExpression(leftOperand));

    public static bool operator ==(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        leftOperand.Context.Provider.CompareTo(leftOperand.Context, leftOperand, rightOperand) == 0;

    public static bool operator !=(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        leftOperand.Context.Provider.CompareTo(leftOperand.Context, leftOperand, rightOperand) != 0;

    public static bool operator <(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        leftOperand.Context.Provider.CompareTo(leftOperand.Context, leftOperand, rightOperand) < 0;

    public static bool operator >(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        leftOperand.Context.Provider.CompareTo(leftOperand.Context, leftOperand, rightOperand) > 0;

    public static bool operator <=(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        leftOperand.Context.Provider.CompareTo(leftOperand.Context, leftOperand, rightOperand) <= 0;

    public static bool operator >=(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        leftOperand.Context.Provider.CompareTo(leftOperand.Context, leftOperand, rightOperand) >= 0;

    public uint[] ToHostArray()
    {
        var hostData = new uint[NumWords];
        Context.Provider.CopyDeviceToHost(hostData, DevicePointer, NumWords);

        return hostData;
    }

    public void CopyFrom(GpuBigInteger source, int destOffsetWords = 0, int srcOffsetWords = 0, int? numWords = null)
    {
        var wordsToCopy = numWords ?? System.Math.Min(NumWords - destOffsetWords, source.NumWords - srcOffsetWords);

        if (wordsToCopy <= 0)
        {
            return;
        }

        var destPtr = IntPtr.Add(DevicePointer, destOffsetWords * 4);
        var srcPtr = IntPtr.Add(source.DevicePointer, srcOffsetWords * 4);

        Context.Provider.CopyDeviceToDevice(destPtr, srcPtr, wordsToCopy);
    }

    public uint[] GetTopWords(int wordCount)
    {
        if (wordCount > NumWords)
        {
            wordCount = NumWords;
        }

        var hostData = new uint[wordCount];
        var srcPtr = IntPtr.Add(DevicePointer, (NumWords - wordCount) * 4);
        Context.Provider.CopyDeviceToHost(hostData, srcPtr, wordCount);
        return hostData;
    }

    public int CompareTo(GpuBigInteger other) => Context.Provider.CompareTo(Context, this, other);

    public override bool Equals(object? obj)
    {
        if (obj is GpuBigInteger other)
        {
            return CompareTo(other) == 0;
        }

        return false;
    }

    public override int GetHashCode()
    {
        // Simple hash code just for the interface (warning: evaluating might be expensive)
        return DevicePointer.GetHashCode();
    }

    public override string ToString() => $"GpuBigInteger(Words={NumWords}, DevicePointer=0x{DevicePointer:X})";

    public void ClearWord(int wordIndex)
    {
        var destPtr = IntPtr.Add(DevicePointer, wordIndex * 4);
        Context.Provider.CopyHostToDevice(destPtr, [0], 1);
    }

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

    private static void ThrowIfSizeMismatch(GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        if (leftOperand.NumWords != rightOperand.NumWords || leftOperand.NumWords != resultBuffer.NumWords)
        {
            throw new ArgumentException(
                $"All buffers must have the same size. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
    }

    private static void ThrowIfSizeMismatch(GpuBigInteger leftOperand, GpuBigInteger resultBuffer)
    {
        if (leftOperand.NumWords != resultBuffer.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must have the same size as input buffer. a: {leftOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }
    }

    public static void BitwiseAnd(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.BitwiseAnd(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    public static void Add(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.Add(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    public static void Sub(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.Sub(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    public static void Mul(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        if (resultBuffer.NumWords != leftOperand.NumWords + rightOperand.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must be the sum of input buffer sizes for Mul. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }

        leftOperand.Context.Provider.Mul(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    public static void ShiftLeft(GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Context.Provider.ShiftLeft(leftOperand.Context, leftOperand, shiftBits, resultBuffer);
    }

    public static void Reciprocal(GpuBigInteger targetOperand, GpuBigInteger resultBuffer)
    {
        var n = targetOperand.NumWords;
        if (resultBuffer.NumWords != 2 * n)
        {
            throw new ArgumentException("Result buffer for Reciprocal must be 2 * b.NumWords.");
        }

        var numBits = n * 32;

        var hostData = targetOperand.ToHostArray();
        var bytes = new byte[n * 4 + 1];
        Buffer.BlockCopy(hostData, 0, bytes, 0, n * 4);
        var bigB = new BigInteger(bytes);

        if (bigB == 0)
        {
            throw new DivideByZeroException();
        }

        var actualBits = 0;
        var temp = bigB;
        while (temp > 0)
        {
            actualBits++;
            temp >>= 1;
        }

        var shift = actualBits - 62;
        if (shift < 0)
        {
            shift = 0;
        }

        var bigTop = bigB >> shift;

        var initialApproximation = (BigInteger.One << 124) / bigTop;
        var finalShift = 2 * numBits - (124 + shift);
        var shiftedApproximation = initialApproximation;
        if (finalShift >= 0)
        {
            shiftedApproximation <<= finalShift;
        }
        else
        {
            shiftedApproximation >>= -finalShift;
        }

        var xData = new uint[2 * n];
        var xBytes = shiftedApproximation.ToByteArray(isUnsigned: true, isBigEndian: false);
        Buffer.BlockCopy(xBytes, 0, xData, 0, System.Math.Min(xBytes.Length, xData.Length * 4));

        resultBuffer.CopyFrom(new GpuBigInteger(targetOperand.Context, xData));

        using var constant = CreatePowerOfTwo(targetOperand.Context, 3 * n, 2 * numBits + 1);
        using var currentProduct = new GpuBigInteger(targetOperand.Context, 3 * n);
        using var currentDifference = new GpuBigInteger(targetOperand.Context, 3 * n);
        using var nextIterationValue = new GpuBigInteger(targetOperand.Context, 5 * n);

        for (var i = 0; i < 15; i++)
        {
            Mul(targetOperand, resultBuffer, currentProduct);
            Sub(constant, currentProduct, currentDifference);
            Mul(resultBuffer, currentDifference, nextIterationValue);

            resultBuffer.CopyFrom(nextIterationValue, destOffsetWords: 0, srcOffsetWords: 2 * n, numWords: 2 * n);
        }
    }

    public static void Div(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);

        var n = rightOperand.NumWords;

        using var reciprocalOfB = new GpuBigInteger(rightOperand.Context, 2 * n);
        Reciprocal(rightOperand, reciprocalOfB);

        using var productWithReciprocal = new GpuBigInteger(rightOperand.Context, 3 * n);
        Mul(leftOperand, reciprocalOfB, productWithReciprocal);
        resultBuffer.CopyFrom(productWithReciprocal, destOffsetWords: 0, srcOffsetWords: 2 * n, numWords: n);
    }

    public static bool IsLessThan(GpuBigInteger leftOperand, GpuBigInteger rightOperand)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand);
        using var temp = new GpuBigInteger(leftOperand.Context, leftOperand.NumWords);
        Sub(leftOperand, rightOperand, temp);
        var topWord = temp.GetTopWords(1)[0];
        return (topWord & 0x80000000u) != 0;
    }

    public static void Sqrt(GpuBigInteger leftOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        var n = leftOperand.NumWords;
        var hostData = leftOperand.ToHostArray();
        var highestBit = -1;
        for (var i = n - 1; i >= 0; i--)
        {
            if (hostData[i] == 0)
            {
                continue;
            }

            var w = hostData[i];
            var b = 31;
            while ((w & (1u << b)) == 0)
            {
                b--;
            }

            highestBit = i * 32 + b;
            break;
        }

        if (highestBit == -1)
        {
            resultBuffer.ClearWord(0);
            resultBuffer.CopyFrom(resultBuffer, numWords: 0);
            var zeros = new uint[n];
            resultBuffer.Context.Provider.CopyHostToDevice(resultBuffer.DevicePointer, zeros, n);
            return;
        }

        using var currentRoot = CreatePowerOfTwo(leftOperand.Context, n, highestBit / 2);
        using var divisionResult = new GpuBigInteger(leftOperand.Context, n);
        using var sumWithDivision = new GpuBigInteger(leftOperand.Context, n);

        for (var i = 0; i < 20; i++)
        {
            Div(leftOperand, currentRoot, divisionResult);
            Add(currentRoot, divisionResult, sumWithDivision);
            ShiftRight(sumWithDivision, 1, currentRoot);
        }

        using var squaredRoot = new GpuBigInteger(leftOperand.Context, 2 * n);
        Mul(currentRoot, currentRoot, squaredRoot);

        var topHalf = new uint[n];
        leftOperand.Context.Provider.CopyDeviceToHost(topHalf, IntPtr.Add(squaredRoot.DevicePointer, n * 4), n);
        var isGreater = false;
        for (var i = 0; i < n; i++)
        {
            if (topHalf[i] == 0)
            {
                continue;
            }

            isGreater = true;
            break;
        }

        if (!isGreater)
        {
            using var lowerSquaredRoot = new GpuBigInteger(leftOperand.Context, n);
            lowerSquaredRoot.CopyFrom(squaredRoot, numWords: n);
            isGreater = IsLessThan(leftOperand, lowerSquaredRoot);
        }

        if (isGreater)
        {
            using var one = new GpuBigInteger(leftOperand.Context, n);
            one.Context.Provider.CopyHostToDevice(one.DevicePointer, [1u], 1);
            Sub(currentRoot, one, resultBuffer);
        }
        else
        {
            resultBuffer.CopyFrom(currentRoot);
        }
    }

    public static void ShiftRight(GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Context.Provider.ShiftRight(leftOperand.Context, leftOperand, shiftBits, resultBuffer);
    }


    public static void BitwiseOr(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.BitwiseOr(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    public static void BitwiseXor(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.BitwiseXor(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    public static void BitwiseNot(GpuBigInteger leftOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Context.Provider.BitwiseNot(leftOperand.Context, leftOperand, resultBuffer);
    }

    public static void DivRem(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger quotientBuffer,
        GpuBigInteger remainderBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, quotientBuffer);
        ThrowIfSizeMismatch(leftOperand, remainderBuffer);

        var n = rightOperand.NumWords;
        using var reciprocalOfB = new GpuBigInteger(rightOperand.Context, 2 * n);
        Reciprocal(rightOperand, reciprocalOfB);

        using var productWithReciprocal = new GpuBigInteger(rightOperand.Context, 3 * n);
        Mul(leftOperand, reciprocalOfB, productWithReciprocal);

        quotientBuffer.CopyFrom(productWithReciprocal, destOffsetWords: 0, srcOffsetWords: 2 * n, numWords: n);

        using var qb = new GpuBigInteger(rightOperand.Context, 2 * n);
        Mul(quotientBuffer, rightOperand, qb);

        using var qbLower = new GpuBigInteger(rightOperand.Context, n);
        qbLower.CopyFrom(qb, numWords: n);

        Sub(leftOperand, qbLower, remainderBuffer);

        using var bPadded = new GpuBigInteger(rightOperand.Context, n);
        bPadded.CopyFrom(rightOperand, numWords: n);
        while (!IsLessThan(remainderBuffer, bPadded))
        {
            Sub(remainderBuffer, bPadded, remainderBuffer);
        }
    }

    public static void Mod(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        using var tempQ = new GpuBigInteger(leftOperand.Context, leftOperand.NumWords);
        DivRem(leftOperand, rightOperand, tempQ, resultBuffer);
    }

    public static void Gcd(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.Gcd(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    public bool Square(int logicalWords) => Context.Provider.Square(Context, DevicePointer, logicalWords);

    public static void ModPow(GpuBigInteger baseVal, GpuBigInteger exp, GpuBigInteger mod, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(baseVal, mod, resultBuffer);

        var n = baseVal.NumWords;
        var expWords = exp.NumWords;

        using var mod2n = new GpuBigInteger(baseVal.Context, 2 * n);
        mod2n.CopyFrom(mod, numWords: n);

        using var tempResult = new GpuBigInteger(baseVal.Context, 2 * n);
        var oneData = new uint[2 * n];
        oneData[0] = 1;
        tempResult.CopyFrom(new GpuBigInteger(baseVal.Context, oneData));

        using var tempBase = new GpuBigInteger(baseVal.Context, 2 * n);
        tempBase.CopyFrom(baseVal, numWords: n);

        // Initial reduction of base
        using var tempBaseQ = new GpuBigInteger(baseVal.Context, 2 * n);
        using var tempBaseR = new GpuBigInteger(baseVal.Context, 2 * n);
        DivRem(tempBase, mod2n, tempBaseQ, tempBaseR);
        tempBase.CopyFrom(tempBaseR);

        using var tempMul = new GpuBigInteger(baseVal.Context, 4 * n);
        using var tempQ = new GpuBigInteger(baseVal.Context, 4 * n);
        using var tempR = new GpuBigInteger(baseVal.Context, 4 * n);

        using var mod4n = new GpuBigInteger(baseVal.Context, 4 * n);
        mod4n.CopyFrom(mod, numWords: n);

        var hostExp = exp.ToHostArray();

        for (int i = 0; i < expWords; i++)
        {
            uint e = hostExp[i];

            // Optimization: if remaining bits are 0, we can break
            if (e == 0)
            {
                bool allZero = true;
                for (int j = i; j < expWords; j++)
                {
                    if (hostExp[j] != 0)
                    {
                        allZero = false;
                        break;
                    }
                }

                if (allZero)
                {
                    break;
                }
            }

            for (int bit = 0; bit < 32; bit++)
            {
                if ((e & 1) == 1)
                {
                    Mul(tempResult, tempBase, tempMul);
                    DivRem(tempMul, mod4n, tempQ, tempR);
                    tempResult.CopyFrom(tempR, numWords: 2 * n);
                }

                Mul(tempBase, tempBase, tempMul);
                DivRem(tempMul, mod4n, tempQ, tempR);
                tempBase.CopyFrom(tempR, numWords: 2 * n);

                e >>= 1;

                // If e becomes 0 and there are no more words, we can break bit loop
                if (e == 0 && i == expWords - 1)
                {
                    break;
                }
            }
        }

        resultBuffer.CopyFrom(tempResult, numWords: n);
    }


    internal void DisposeCore()
    {
        if (_disposed)
        {
            return;
        }

        if (DevicePointer != IntPtr.Zero)
        {
            Context.Provider.FreeMemory(DevicePointer);

            DevicePointer = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

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
        Dispose();
    }


    public bool IsZero() => Context.Provider.IsZero(Context, this);
    public bool IsOne() => Context.Provider.IsOne(Context, this);

    public int GetBitLength() => Context.Provider.GetBitLength(Context, this);
    public int GetByteCount() => (GetBitLength() + 7) / 8;

    public bool IsEven
    {
        get
        {
            var word = new uint[1];
            Context.Provider.CopyDeviceToHost(word, DevicePointer, 1);
            return (word[0] & 1) == 0;
        }
    }

    public bool IsNegative
    {
        get
        {
            var word = new uint[1];
            Context.Provider.CopyDeviceToHost(word, IntPtr.Add(DevicePointer, (NumWords - 1) * 4), 1);
            return (word[0] & 0x80000000) != 0;
        }
        set
        {
            var word = new uint[1];
            var ptr = IntPtr.Add(DevicePointer, (NumWords - 1) * 4);
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

    public static void Add(GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        leftOperand.Context.Provider.Add(leftOperand.Context, leftOperand, rightOperand, resultBuffer);

    public static void Sub(GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        leftOperand.Context.Provider.Sub(leftOperand.Context, leftOperand, rightOperand, resultBuffer);

    public static void Mul(GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        leftOperand.Context.Provider.Mul(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
}

