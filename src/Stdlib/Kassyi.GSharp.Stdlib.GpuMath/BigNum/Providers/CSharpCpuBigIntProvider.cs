namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Provides a reference host (CPU) execution provider for arbitrary-precision large integer arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Implements <see cref="IGpuExecutionProvider"/> by delegating computations to <see cref="System.Numerics.BigInteger"/> on host memory.
/// Used for Tier-1 deterministic unit testing, golden-master verification, and systems without CUDA-capable GPUs.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> Memory allocation methods allocate unmanaged host heap blocks via <see cref="Marshal.AllocHGlobal(nint)"/>.
/// </para>
/// <para>
/// <b>Thread Safety:</b> Stateless and thread-safe. Graph provider operations are no-ops.
/// </para>
/// </remarks>
public sealed class CSharpCpuBigIntProvider : IGpuExecutionProvider
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="CSharpCpuBigIntProvider"/>.
    /// </summary>
    public static readonly CSharpCpuBigIntProvider Instance = new();

    private CSharpCpuBigIntProvider()
    {
    }

    /// <summary>
    /// Reads an unsigned big integer from unmanaged host memory.
    /// </summary>
    // [WHY]: Converts raw limb memory buffers into System.Numerics.BigInteger for CPU reference arithmetic.
    private static unsafe BigInteger Read(IntPtr ptr, int numWords)
    {
        if (numWords <= 0)
        {
            return BigInteger.Zero;
        }

        var span = new ReadOnlySpan<byte>((void*)ptr, numWords * 4);
        return new BigInteger(span, isUnsigned: true, isBigEndian: false);
    }

    /// <summary>
    /// Writes an unsigned big integer to unmanaged host memory, zero-padding to the maximum word count.
    /// </summary>
    // [WHY]: Serializes System.Numerics.BigInteger back to limb format in host memory buffers.
    private static unsafe void Write(BigInteger value, IntPtr ptr, int maxWords)
    {
        if (maxWords <= 0)
        {
            return;
        }

        Unsafe.InitBlock((void*)ptr, 0, (uint)(maxWords * 4));

        var mask = (BigInteger.One << (maxWords * 32)) - 1;
        value &= mask;

        if (value.IsZero)
        {
            return;
        }

        var destSpan = new Span<byte>((void*)ptr, maxWords * 4);
        value.TryWriteBytes(destSpan, out var _, isUnsigned: true, isBigEndian: false);
    }


    // --- IGpuMemoryProvider ---

    /// <inheritdoc/>
    public IntPtr AllocateMemory(int numWords) => Marshal.AllocHGlobal(numWords * 4);

    /// <inheritdoc/>
    /// <remarks>Host memory is freed immediately; <paramref name="stream"/> is ignored.</remarks>
    public void FreeMemory(IntPtr devicePointer, IntPtr stream = 0)
    {
        if (devicePointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(devicePointer);
        }
    }

    /// <inheritdoc/>
    public unsafe void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream)
    {
        if (numWords > 0 && devicePointer != IntPtr.Zero)
        {
            Unsafe.InitBlock((void*)devicePointer, 0, (uint)(numWords * 4));
        }
    }

    /// <inheritdoc/>
    public unsafe void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0)
    {
        if (numWords <= 0 || hostData.Length == 0)
        {
            return;
        }

        fixed (uint* pHost = hostData)
        {
            Buffer.MemoryCopy(pHost, (void*)devicePointer, numWords * 4,
                Math.Min(hostData.Length, numWords) * 4);
        }
    }

    /// <inheritdoc/>
    public unsafe void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0)
    {
        if (numWords <= 0 || hostData.Length == 0)
        {
            return;
        }

        fixed (uint* pHost = hostData)
        {
            Buffer.MemoryCopy((void*)devicePointer, pHost, hostData.Length * 4,
                Math.Min(hostData.Length, numWords) * 4);
        }
    }

    /// <inheritdoc/>
    public unsafe void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0)
    {
        if (numWords > 0 && destPointer != srcPointer)
        {
            Buffer.MemoryCopy((void*)srcPointer, (void*)destPointer, numWords * 4, numWords * 4);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Host memory is accessed directly; <paramref name="stream"/> is ignored.</remarks>
    public unsafe uint ReadWord(IntPtr devicePointer, int wordOffset = 0, IntPtr stream = 0) =>
        *(uint*)IntPtr.Add(devicePointer, wordOffset * 4);

    /// <inheritdoc/>
    /// <remarks>Host memory is accessed directly; <paramref name="stream"/> is ignored.</remarks>
    public unsafe void WriteWord(IntPtr devicePointer, int wordOffset, uint value, IntPtr stream = 0) =>
        *(uint*)IntPtr.Add(devicePointer, wordOffset * 4) = value;

    /// <inheritdoc/>
    public void SynchronizeStream(IntPtr stream)
    {
        // No-op in CPU mode
    }

    /// <inheritdoc/>
    /// <remarks>Nothing runs asynchronously in CPU mode; the returned task is already completed.</remarks>
    public ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled(cancellationToken) : ValueTask.CompletedTask;

    /// <inheritdoc/>
    /// <remarks>
    /// Host memory is copied synchronously; the returned task is already completed and <paramref name="stream"/> is ignored.
    /// </remarks>
    public unsafe ValueTask CopyDeviceToHostAsync(Memory<uint> hostData, IntPtr devicePointer, int numWords,
        IntPtr stream, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(numWords, hostData.Length);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        if (numWords > 0)
        {
            new ReadOnlySpan<uint>((void*)devicePointer, numWords).CopyTo(hostData.Span);
        }

        return ValueTask.CompletedTask;
    }

    // --- IGpuBasicMathProvider ---

    /// <inheritdoc/>
    public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) +
                  Read(rightOperand.DevicePointer, rightOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    /// <inheritdoc/>
    public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) -
                  Read(rightOperand.DevicePointer, rightOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    /// <inheritdoc/>
    public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) + rightOperand;
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    /// <inheritdoc/>
    public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) - rightOperand;
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }


    /// <inheritdoc/>
    public void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) &
                  Read(rightOperand.DevicePointer, rightOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    /// <inheritdoc/>
    public void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) |
                  Read(rightOperand.DevicePointer, rightOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    /// <inheritdoc/>
    public void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) ^
                  Read(rightOperand.DevicePointer, rightOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    /// <inheritdoc/>
    public void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer)
    {
        var val = ~Read(leftOperand.DevicePointer, leftOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    /// <inheritdoc/>
    public void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) << shiftBits;
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    /// <inheritdoc/>
    public void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) >> shiftBits;
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    /// <inheritdoc/>
    public bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords)
    {
        var val = Read(devicePointer, logicalWords);
        Write(val * val, devicePointer, logicalWords * 2);
        return true;
    }

    /// <inheritdoc/>
    public bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand) =>
        Read(leftOperand.DevicePointer, leftOperand.NumWords).IsZero;

    /// <inheritdoc/>
    public bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand) =>
        Read(leftOperand.DevicePointer, leftOperand.NumWords).IsOne;

    /// <inheritdoc/>
    public int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand) =>
        (int)Read(leftOperand.DevicePointer, leftOperand.NumWords).GetBitLength();

    /// <inheritdoc/>
    public int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand)
    {
        return Read(leftOperand.DevicePointer, leftOperand.NumWords)
            .CompareTo(Read(rightOperand.DevicePointer, rightOperand.NumWords));
    }

    /// <inheritdoc/>
    public uint DivideByUInt32(GpuWorkspace workspace, GpuBigUInt dividend, uint divisor,
        GpuBigUInt quotientBuffer)
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }

        var val = Read(dividend.DevicePointer, dividend.NumWords);
        var quot = BigInteger.DivRem(val, divisor, out var rem);
        Write(quot, quotientBuffer.DevicePointer, quotientBuffer.NumWords);
        return (uint)rem;
    }

    // --- IGpuAdvancedMathProvider ---

    /// <inheritdoc/>
    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) *
                  Read(rightOperand.DevicePointer, rightOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    /// <inheritdoc/>
    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) * rightOperand;
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    /// <inheritdoc/>
    public void InverseSqrtMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int actualBits, int iterations)
    {
        var x = Read(targetOperand.DevicePointer, targetOperand.NumWords);
        if (x.IsZero)
        {
            throw new DivideByZeroException();
        }

        var b = (targetOperand.NumWords + marginWords) * 32;
        var ySquared = (BigInteger.One << (2 * b)) / x;
        var result = ySquared.Sqrt();
        Write(result, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    /// <inheritdoc/>
    public void ReciprocalMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int numBits, int marginBits, int iterations, int targetBits)
    {
        var x = Read(targetOperand.DevicePointer, targetOperand.NumWords);
        if (x.IsZero)
        {
            throw new DivideByZeroException();
        }

        var constant = BigInteger.One << targetBits;
        var result = constant / x;
        Write(result, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }


    /// <inheritdoc/>
    public void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = BigInteger.GreatestCommonDivisor(Read(leftOperand.DevicePointer, leftOperand.NumWords),
            Read(rightOperand.DevicePointer, rightOperand.NumWords));
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    /// <inheritdoc/>
    public void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer)

    {
        var a = Read(value.DevicePointer, value.NumWords);
        var n = Read(modulus.DevicePointer, modulus.NumWords);

        BigInteger i = n, v = 0, d = 1;
        while (a > 0)
        {
            BigInteger t = i / a, x = a;
            a = i % x;
            i = x;
            x = d;
            d = v - t * x;
            v = x;
        }
        if (i != 1)
        {
            throw new ArithmeticException("ModInverse failed: The two numbers are not coprime (GCD is not 1).");
        }
        v %= n;
        if (v < 0)
        {
            v = (v + n) % n;
        }

        Write(v, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    // --- IGpuGraphProvider (No-op on CPU fallback) ---

    /// <summary>
    /// Performs no operation. CUDA Graph stream capture is not supported or required in CPU fallback mode.
    /// </summary>
    /// <param name="stream">The unmanaged stream handle (ignored in CPU fallback mode).</param>
    /// <remarks>
    /// <para>
    /// <b>Execution Target:</b> Host-only CPU fallback. In CPU mode, all operations execute immediately and synchronously.
    /// </para>
    /// </remarks>
    public void BeginCapture(IntPtr stream)
    {
        // No-op
    }

    /// <summary>
    /// Ends graph capture in CPU fallback mode and returns a dummy handle.
    /// </summary>
    /// <param name="stream">The unmanaged stream handle being captured (ignored in CPU fallback mode).</param>
    /// <returns>Always returns <see cref="IntPtr.Zero"/> as a dummy graph handle in CPU fallback mode.</returns>
    /// <remarks>
    /// <para>
    /// <b>Execution Target:</b> Host-only CPU fallback. No unmanaged CUDA graph topology is instantiated on the host.
    /// </para>
    /// </remarks>
    public IntPtr EndCapture(IntPtr stream) => IntPtr.Zero;

    /// <summary>
    /// Instantiates an executable graph instance in CPU fallback mode and returns a dummy handle.
    /// </summary>
    /// <param name="graph">The captured graph handle (ignored in CPU fallback mode).</param>
    /// <returns>Always returns <see cref="IntPtr.Zero"/> as a dummy executable graph handle.</returns>
    /// <remarks>
    /// <para>
    /// <b>Execution Target:</b> Host-only CPU fallback.
    /// </para>
    /// </remarks>
    public IntPtr InstantiateGraph(IntPtr graph) => IntPtr.Zero;

    /// <summary>
    /// Performs no operation. Graph execution is not applicable in CPU fallback mode.
    /// </summary>
    /// <param name="graphExec">The executable graph handle (ignored in CPU fallback mode).</param>
    /// <param name="stream">The target stream for execution (ignored in CPU fallback mode).</param>
    /// <remarks>
    /// <para>
    /// <b>Execution Target:</b> Host-only CPU fallback. Operations are evaluated synchronously during expression evaluation.
    /// </para>
    /// </remarks>
    public void LaunchGraph(IntPtr graphExec, IntPtr stream)
    {
        // No-op
    }

    /// <summary>
    /// Performs no operation. Destroys dummy graph handles in CPU fallback mode.
    /// </summary>
    /// <param name="graph">The captured graph handle (ignored in CPU fallback mode).</param>
    /// <param name="graphExec">The executable graph handle (ignored in CPU fallback mode).</param>
    /// <remarks>
    /// <para>
    /// <b>Execution Target:</b> Host-only CPU fallback. No unmanaged memory or native CUDA driver resources are held.
    /// </para>
    /// </remarks>
    public void DestroyGraph(IntPtr graph, IntPtr graphExec)
    {
        // No-op
    }
}
