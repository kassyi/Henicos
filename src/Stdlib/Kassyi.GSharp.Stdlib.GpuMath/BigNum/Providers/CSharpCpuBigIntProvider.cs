namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Host (CPU) fallback execution provider for arbitrary-precision BigInteger arithmetic.
/// Operates on unmanaged host memory using System.Numerics.BigInteger.
/// </summary>
public sealed class CSharpCpuBigIntProvider : IGpuExecutionProvider
{
    public static readonly CSharpCpuBigIntProvider Instance = new();

    private CSharpCpuBigIntProvider()
    {
    }

    private static unsafe BigInteger Read(IntPtr ptr, int numWords)
    {
        if (numWords <= 0)
        {
            return BigInteger.Zero;
        }

        var span = new ReadOnlySpan<byte>((void*)ptr, numWords * 4);
        return new BigInteger(span, isUnsigned: true, isBigEndian: false);
    }

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

    public IntPtr AllocateMemory(int numWords) => Marshal.AllocHGlobal(numWords * 4);

    public void FreeMemory(IntPtr devicePointer)
    {
        if (devicePointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(devicePointer);
        }
    }

    public unsafe void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream)
    {
        if (numWords > 0 && devicePointer != IntPtr.Zero)
        {
            Unsafe.InitBlock((void*)devicePointer, 0, (uint)(numWords * 4));
        }
    }

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

    public unsafe void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0)
    {
        if (numWords > 0 && destPointer != srcPointer)
        {
            Buffer.MemoryCopy((void*)srcPointer, (void*)destPointer, numWords * 4, numWords * 4);
        }
    }

    public unsafe uint ReadWord(IntPtr devicePointer, int wordOffset = 0) =>
        *(uint*)IntPtr.Add(devicePointer, wordOffset * 4);

    public unsafe void WriteWord(IntPtr devicePointer, int wordOffset, uint value) =>
        *(uint*)IntPtr.Add(devicePointer, wordOffset * 4) = value;

    // --- IGpuBasicMathProvider ---

    public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) +
                  Read(rightOperand.DevicePointer, rightOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) -
                  Read(rightOperand.DevicePointer, rightOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) + rightOperand;
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) - rightOperand;
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    public void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) &
                  Read(rightOperand.DevicePointer, rightOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    public void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) |
                  Read(rightOperand.DevicePointer, rightOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    public void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) ^
                  Read(rightOperand.DevicePointer, rightOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    public void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer)
    {
        var val = ~Read(leftOperand.DevicePointer, leftOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    public void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) << shiftBits;
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    public void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) >> shiftBits;
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    public bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords)
    {
        var val = Read(devicePointer, logicalWords);
        Write(val * val, devicePointer, logicalWords * 2);
        return true;
    }

    public bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand) =>
        Read(leftOperand.DevicePointer, leftOperand.NumWords).IsZero;

    public bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand) =>
        Read(leftOperand.DevicePointer, leftOperand.NumWords).IsOne;

    public int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand) =>
        (int)Read(leftOperand.DevicePointer, leftOperand.NumWords).GetBitLength();

    public int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand)
    {
        return Read(leftOperand.DevicePointer, leftOperand.NumWords)
            .CompareTo(Read(rightOperand.DevicePointer, rightOperand.NumWords));
    }

    // --- IGpuAdvancedMathProvider ---

    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) *
                  Read(rightOperand.DevicePointer, rightOperand.NumWords);
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = Read(leftOperand.DevicePointer, leftOperand.NumWords) * rightOperand;
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

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
        var result = Sqrt(ySquared);
        Write(result, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    public void ReciprocalMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int numBits, int marginBits, int iterations, int targetBits)
    {
        var x = Read(targetOperand.DevicePointer, targetOperand.NumWords);
        if (x.IsZero)
        {
            throw new DivideByZeroException();
        }

        var constant = BigInteger.One << (targetBits + numBits);
        var result = constant / x;
        Write(result, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    private static BigInteger Sqrt(BigInteger n)
    {
        if (n.IsZero)
        {
            return BigInteger.Zero;
        }

        var bitLength = (int)n.GetBitLength();
        var root = BigInteger.One << (bitLength / 2);
        
        var lastRoot = root;
        while (true)
        {
            root = (root + n / root) >> 1;
            if (root >= lastRoot)
            {
                if (root - lastRoot <= 1 && (root * root) > n)
                {
                    return lastRoot;
                }

                if (root == lastRoot)
                {
                    return root;
                }
            }
            lastRoot = root;
        }
    }

    public void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var val = BigInteger.GreatestCommonDivisor(Read(leftOperand.DevicePointer, leftOperand.NumWords),
            Read(rightOperand.DevicePointer, rightOperand.NumWords));
        Write(val, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

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
        if (v < 0) v = (v + n) % n;
        
        Write(v, resultBuffer.DevicePointer, resultBuffer.NumWords);
    }

    // --- IGpuGraphProvider (No-op on CPU fallback) ---

    /// <summary>
    /// No-op in CPU fallback mode; CUDA Graph stream capture is not supported or required on CPU.
    /// </summary>
    public void BeginCapture(IntPtr stream)
    {
        // No-op
    }

    /// <summary>
    /// Returns <see cref="IntPtr.Zero"/> as a dummy graph handle in CPU fallback mode.
    /// </summary>
    public IntPtr EndCapture(IntPtr stream) => IntPtr.Zero;

    /// <summary>
    /// Returns <see cref="IntPtr.Zero"/> as a dummy executable graph handle in CPU fallback mode.
    /// </summary>
    public IntPtr InstantiateGraph(IntPtr graph) => IntPtr.Zero;

    /// <summary>
    /// No-op in CPU fallback mode.
    /// </summary>
    public void LaunchGraph(IntPtr graphExec, IntPtr stream)
    {
        // No-op
    }

    /// <summary>
    /// No-op in CPU fallback mode.
    /// </summary>
    public void DestroyGraph(IntPtr graph, IntPtr graphExec)
    {
        // No-op
    }
}
