using Kassyi.GSharp.Stdlib.Math.Expressions;

namespace Kassyi.GSharp.Stdlib.Math;

/// <summary>
/// Represents an arbitrary-precision unsigned integer stored in NVIDIA GPU global device memory.
/// </summary>
/// <remarks>
/// <para>All arithmetic and bitwise operations are executed on the GPU using SIMD/warp-level intrinsics.</para>
/// <para><b>Memory Management:</b> Implements <see cref="IDisposable"/> to ensure deterministic GPU memory deallocation.</para>
/// </remarks>
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

    private bool _disposed;

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

        if (DevicePointer == IntPtr.Zero)
        {
            throw new OutOfMemoryException(
                $"Failed to allocate GPU memory for {numWords} words ({numWords * 4} bytes).");
        }
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
    public static bool operator ==(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        leftOperand.Context.Provider.CompareTo(leftOperand.Context, leftOperand, rightOperand) == 0;

    /// <summary>Determines whether two big integers represent different values on the GPU.</summary>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns><c>true</c> if not equal; otherwise, <c>false</c>.</returns>
    /// <remarks>This evaluates synchronously by triggering a GPU device memory comparison.</remarks>
    public static bool operator !=(GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        leftOperand.Context.Provider.CompareTo(leftOperand.Context, leftOperand, rightOperand) != 0;

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

        var destPtr = IntPtr.Add(DevicePointer, destOffsetWords * 4);
        var srcPtr = IntPtr.Add(source.DevicePointer, srcOffsetWords * 4);

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
        var srcPtr = IntPtr.Add(DevicePointer, (NumWords - wordCount) * 4);
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
        var destPtr = IntPtr.Add(DevicePointer, wordIndex * 4);
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

    /// <summary>Performs bitwise AND (<c>leftOperand &amp; rightOperand</c>) on GPU and stores result in destination buffer.</summary>
    public static void BitwiseAnd(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.BitwiseAnd(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Computes arbitrary-precision addition (<c>leftOperand + rightOperand</c>) on the GPU.</summary>
    public static void Add(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.Add(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Computes arbitrary-precision subtraction (<c>leftOperand - rightOperand</c>) on the GPU.</summary>
    public static void Sub(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.Sub(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>
    /// Computes full arbitrary-precision multiplication (<c>leftOperand * rightOperand</c>) on the GPU.
    /// </summary>
    /// <remarks>The destination buffer must have length equal to <c>leftOperand.NumWords + rightOperand.NumWords</c>.</remarks>
    public static void Mul(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        if (resultBuffer.NumWords != leftOperand.NumWords + rightOperand.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must be the sum of input buffer sizes for Mul. a: {leftOperand.NumWords}, b: {rightOperand.NumWords}, result: {resultBuffer.NumWords} words.");
        }

        leftOperand.Context.Provider.Mul(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Shifts the big integer left by the specified bit count on the GPU.</summary>
    public static void ShiftLeft(GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Context.Provider.ShiftLeft(leftOperand.Context, leftOperand, shiftBits, resultBuffer);
    }

    /// <summary>
    /// Computes the reciprocal approximation of the operand using Newton-Raphson iterations on the GPU.
    /// </summary>
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

    /// <summary>Computes arbitrary-precision division (<c>leftOperand / rightOperand</c>) on the GPU.</summary>
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

    /// <summary>Determines whether <paramref name="leftOperand"/> is strictly less than <paramref name="rightOperand"/> on the GPU.</summary>
    public static bool IsLessThan(GpuBigInteger leftOperand, GpuBigInteger rightOperand)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand);
        using var temp = new GpuBigInteger(leftOperand.Context, leftOperand.NumWords);
        Sub(leftOperand, rightOperand, temp);
        var topWord = temp.GetTopWords(1)[0];
        return (topWord & 0x80000000u) != 0;
    }

    /// <summary>Computes the integer square root (<c>floor(sqrt(leftOperand))</c>) on the GPU.</summary>
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

    /// <summary>Shifts the big integer right by the specified bit count on the GPU.</summary>
    public static void ShiftRight(GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Context.Provider.ShiftRight(leftOperand.Context, leftOperand, shiftBits, resultBuffer);
    }

    /// <summary>Performs bitwise OR on the GPU.</summary>
    public static void BitwiseOr(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.BitwiseOr(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Performs bitwise XOR on the GPU.</summary>
    public static void BitwiseXor(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.BitwiseXor(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
    }

    /// <summary>Performs bitwise NOT on the GPU.</summary>
    public static void BitwiseNot(GpuBigInteger leftOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, resultBuffer);
        leftOperand.Context.Provider.BitwiseNot(leftOperand.Context, leftOperand, resultBuffer);
    }

    /// <summary>Computes simultaneous division quotient and remainder on the GPU.</summary>
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

    /// <summary>Computes arbitrary-precision modulo (<c>leftOperand % rightOperand</c>) on the GPU.</summary>
    public static void Mod(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        using var tempQ = new GpuBigInteger(leftOperand.Context, leftOperand.NumWords);
        DivRem(leftOperand, rightOperand, tempQ, resultBuffer);
    }

    /// <summary>Computes the Greatest Common Divisor (GCD) of two big integers on the GPU.</summary>
    public static void Gcd(GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(leftOperand, rightOperand, resultBuffer);
        leftOperand.Context.Provider.Gcd(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
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

    /// <summary>Computes modular exponentiation (<c>baseVal^exp mod mod</c>) on the GPU.</summary>
    public static void ModPow(GpuBigInteger baseVal, GpuBigInteger exp, GpuBigInteger mod, GpuBigInteger resultBuffer)
    {
        ThrowIfSizeMismatch(baseVal, mod, resultBuffer);

        var n = baseVal.NumWords;
        var expWords = exp.NumWords;

        using var mod2N = new GpuBigInteger(baseVal.Context, 2 * n);
        mod2N.CopyFrom(mod, numWords: n);

        using var tempResult = new GpuBigInteger(baseVal.Context, 2 * n);
        var oneData = new uint[2 * n];
        oneData[0] = 1;
        tempResult.CopyFrom(new GpuBigInteger(baseVal.Context, oneData));

        using var tempBase = new GpuBigInteger(baseVal.Context, 2 * n);
        tempBase.CopyFrom(baseVal, numWords: n);

        // Initial reduction of base
        using var tempBaseQ = new GpuBigInteger(baseVal.Context, 2 * n);
        using var tempBaseR = new GpuBigInteger(baseVal.Context, 2 * n);
        DivRem(tempBase, mod2N, tempBaseQ, tempBaseR);
        tempBase.CopyFrom(tempBaseR);

        using var tempMul = new GpuBigInteger(baseVal.Context, 4 * n);
        using var tempQ = new GpuBigInteger(baseVal.Context, 4 * n);
        using var tempR = new GpuBigInteger(baseVal.Context, 4 * n);

        using var mod4N = new GpuBigInteger(baseVal.Context, 4 * n);
        mod4N.CopyFrom(mod, numWords: n);

        var hostExp = exp.ToHostArray();

        for (var i = 0; i < expWords; i++)
        {
            var e = hostExp[i];

            // Optimization: if remaining bits are 0, we can break
            if (e == 0)
            {
                var allZero = true;
                for (var j = i; j < expWords; j++)
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

            for (var bit = 0; bit < 32; bit++)
            {
                if ((e & 1) == 1)
                {
                    Mul(tempResult, tempBase, tempMul);
                    DivRem(tempMul, mod4N, tempQ, tempR);
                    tempResult.CopyFrom(tempR, numWords: 2 * n);
                }

                Mul(tempBase, tempBase, tempMul);
                DivRem(tempMul, mod4N, tempQ, tempR);
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

        _disposed = true;

        if (DevicePointer != IntPtr.Zero)
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
        Dispose();
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

    /// <summary>Adds a scalar 32-bit unsigned integer to <paramref name="leftOperand"/> on the GPU.</summary>
    public static void Add(GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        leftOperand.Context.Provider.Add(leftOperand.Context, leftOperand, rightOperand, resultBuffer);

    /// <summary>Subtracts a scalar 32-bit unsigned integer from <paramref name="leftOperand"/> on the GPU.</summary>
    public static void Sub(GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        leftOperand.Context.Provider.Sub(leftOperand.Context, leftOperand, rightOperand, resultBuffer);

    /// <summary>Multiplies <paramref name="leftOperand"/> by a scalar 32-bit unsigned integer on the GPU.</summary>
    public static void Mul(GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        leftOperand.Context.Provider.Mul(leftOperand.Context, leftOperand, rightOperand, resultBuffer);
}

