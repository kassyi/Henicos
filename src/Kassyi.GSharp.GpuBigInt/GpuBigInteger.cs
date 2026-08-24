
using System.Numerics;
using Kassyi.GSharp.GpuBigInt.Expressions;

namespace Kassyi.GSharp.GpuBigInt;

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
        if (targetResult == null || targetResult == this)
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

    public static GpuBigIntExpr operator +(GpuBigInteger a, GpuBigInteger b) => new(new AddExpression(a, b));
    public static GpuBigIntExpr operator -(GpuBigInteger a, GpuBigInteger b) => new(new SubExpression(a, b));
    public static GpuBigIntExpr operator *(GpuBigInteger a, GpuBigInteger b) => new(new MulExpression(a, b));
    public static GpuBigIntExpr operator /(GpuBigInteger a, GpuBigInteger b) => new(new DivExpression(a, b));
    public static GpuBigIntExpr operator &(GpuBigInteger a, GpuBigInteger b) => new(new BitwiseAndExpression(a, b));

    public static GpuBigIntExpr operator <<(GpuBigInteger a, int shiftBits) =>
        new(new ShiftLeftExpression(a, shiftBits));

    public static GpuBigIntExpr operator >> (GpuBigInteger a, int shiftBits) =>
        new(new ShiftRightExpression(a, shiftBits));

    public uint[] ToHostArray()
    {
        var hostData = new uint[NumWords];
        Context.Provider.CopyDeviceToHost(hostData, DevicePointer, NumWords);

        return hostData;
    }

    public void CopyFrom(GpuBigInteger source, int destOffsetWords = 0, int srcOffsetWords = 0, int? numWords = null)
    {
        var wordsToCopy = numWords ?? Math.Min(NumWords - destOffsetWords, source.NumWords - srcOffsetWords);

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

    private static void ThrowIfSizeMismatch(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        if (a.NumWords != b.NumWords || a.NumWords != result.NumWords)
        {
            throw new ArgumentException(
                $"All buffers must have the same size. a: {a.NumWords}, b: {b.NumWords}, result: {result.NumWords} words.");
        }
    }

    private static void ThrowIfSizeMismatch(GpuBigInteger a, GpuBigInteger result)
    {
        if (a.NumWords != result.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must have the same size as input buffer. a: {a.NumWords}, result: {result.NumWords} words.");
        }
    }

    public static void BitwiseAnd(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        ThrowIfSizeMismatch(a, b, result);
        a.Context.Provider.BitwiseAnd(a.Context, a, b, result);
    }

    public static void Add(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        ThrowIfSizeMismatch(a, b, result);
        a.Context.Provider.Add(a.Context, a, b, result);
    }

    public static void Sub(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        ThrowIfSizeMismatch(a, b, result);
        a.Context.Provider.Sub(a.Context, a, b, result);
    }

    public static void Mul(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        if (result.NumWords != a.NumWords + b.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must be the sum of input buffer sizes for Mul. a: {a.NumWords}, b: {b.NumWords}, result: {result.NumWords} words.");
        }

        a.Context.Provider.Mul(a.Context, a, b, result);
    }

    public static void ShiftLeft(GpuBigInteger a, int shiftBits, GpuBigInteger result)
    {
        ThrowIfSizeMismatch(a, result);
        a.Context.Provider.ShiftLeft(a.Context, a, shiftBits, result);
    }

    public static void Reciprocal(GpuBigInteger b, GpuBigInteger result)
    {
        var n = b.NumWords;
        if (result.NumWords != 2 * n)
        {
            throw new ArgumentException("Result buffer for Reciprocal must be 2 * b.NumWords.");
        }

        var numBits = n * 32;

        var hostData = b.ToHostArray();
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
        // We want X = 2^(2*numBits) / B
        // initialApproximation is roughly 2^124 / (B / 2^shift) = 2^(124+shift) / B
        // We need to shift initialApproximation by 2*numBits - (124+shift)
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
        Buffer.BlockCopy(xBytes, 0, xData, 0, Math.Min(xBytes.Length, xData.Length * 4));

        result.CopyFrom(new GpuBigInteger(b.Context, xData));

        using var constant = CreatePowerOfTwo(b.Context, 3 * n, 2 * numBits + 1);
        using var currentProduct = new GpuBigInteger(b.Context, 3 * n);
        using var currentDifference = new GpuBigInteger(b.Context, 3 * n);
        using var nextIterationValue = new GpuBigInteger(b.Context, 5 * n);

        for (var i = 0; i < 15; i++)
        {
            Mul(b, result, currentProduct);
            Sub(constant, currentProduct, currentDifference);
            Mul(result, currentDifference, nextIterationValue);

            // nextIterationValue >> (2 * numBits) => shift right by 2n words
            result.CopyFrom(nextIterationValue, destOffsetWords: 0, srcOffsetWords: 2 * n, numWords: 2 * n);
        }
    }

    public static void Div(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        ThrowIfSizeMismatch(a, b, result);

        var n = b.NumWords;

        using var reciprocalOfB = new GpuBigInteger(b.Context, 2 * n);
        Reciprocal(b, reciprocalOfB);

        using var productWithReciprocal = new GpuBigInteger(b.Context, 3 * n);
        Mul(a, reciprocalOfB, productWithReciprocal);

        // Q = productWithReciprocal >> (2 * n words)
        result.CopyFrom(productWithReciprocal, destOffsetWords: 0, srcOffsetWords: 2 * n, numWords: n);
    }

    public static bool IsLessThan(GpuBigInteger a, GpuBigInteger b)
    {
        ThrowIfSizeMismatch(a, b);
        using var temp = new GpuBigInteger(a.Context, a.NumWords);
        Sub(a, b, temp);
        var topWord = temp.GetTopWords(1)[0];
        return (topWord & 0x80000000u) != 0;
    }

    public static void Sqrt(GpuBigInteger a, GpuBigInteger result)
    {
        ThrowIfSizeMismatch(a, result);
        var n = a.NumWords;

        // Estimate initial X
        // We need to find the highest set bit in A
        var hostData = a.ToHostArray();
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
            result.ClearWord(0);
            result.CopyFrom(result, numWords: 0); // clear all
            var zeros = new uint[n];
            result.Context.Provider.CopyHostToDevice(result.DevicePointer, zeros, n);
            return;
        }

        using var currentRoot = CreatePowerOfTwo(a.Context, n, highestBit / 2);
        using var divisionResult = new GpuBigInteger(a.Context, n); // A / currentRoot
        using var sumWithDivision = new GpuBigInteger(a.Context, n); // currentRoot + A / currentRoot

        for (var i = 0; i < 20; i++)
        {
            Div(a, currentRoot, divisionResult);
            Add(currentRoot, divisionResult, sumWithDivision);
            ShiftRight(sumWithDivision, 1, currentRoot);
        }

        // Fix-up: if X * X > A, X = X - 1
        using var squaredRoot = new GpuBigInteger(a.Context, 2 * n);
        Mul(currentRoot, currentRoot, squaredRoot);

        // We only compare the lower n words of squaredRoot since if it's > A, it's either in top half or lower half.
        // Actually A is n words, squaredRoot is 2n words. A is implicitly padded with 0.
        // A simpler way: if top n words of squaredRoot are non-zero, squaredRoot > A.
        var topHalf = new uint[n];
        a.Context.Provider.CopyDeviceToHost(topHalf, IntPtr.Add(squaredRoot.DevicePointer, n * 4), n);
        var isGreater = false;
        for (var i = 0; i < n; i++)
        {
            if (topHalf[i] != 0)
            {
                isGreater = true;
                break;
            }
        }

        if (!isGreater)
        {
            using var lowerSquaredRoot = new GpuBigInteger(a.Context, n);
            lowerSquaredRoot.CopyFrom(squaredRoot, numWords: n);
            isGreater = IsLessThan(a, lowerSquaredRoot);
        }

        if (isGreater)
        {
            using var one = new GpuBigInteger(a.Context, n);
            one.Context.Provider.CopyHostToDevice(one.DevicePointer, [1u], 1); // rest is 0
            Sub(currentRoot, one, result);
        }
        else
        {
            result.CopyFrom(currentRoot);
        }
    }

    public static void ShiftRight(GpuBigInteger a, int shiftBits, GpuBigInteger result)
    {
        ThrowIfSizeMismatch(a, result);
        a.Context.Provider.ShiftRight(a.Context, a, shiftBits, result);
    }

    public bool Square(int logicalWords) => Context.Provider.Square(Context, DevicePointer, logicalWords);

    internal void DisposeCore()
    {
        if (!_disposed)
        {
            if (DevicePointer != IntPtr.Zero)
            {
                Context.Provider.FreeMemory(DevicePointer);

                DevicePointer = IntPtr.Zero;
            }

            _disposed = true;
        }

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
}

