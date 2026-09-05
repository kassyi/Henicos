using System.Collections.Concurrent;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Jit;

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Providers;

/// <summary>
/// Execution provider implementing arbitrary-precision BigInteger arithmetic via GSharp JIT transpiled GPU kernels.
/// </summary>
/// <remarks><para>Implements: <b>GEN-49, GEN-50</b></para></remarks>
public sealed class GSharpJitProvider : IGpuExecutionProvider
{
    /// <summary>Gets the singleton instance of the <see cref="GSharpJitProvider"/>.</summary>
    public static readonly GSharpJitProvider Instance = new();

    private class TwiddleCache
    {
        /// <inheritdoc/>
        public IntPtr Forward1;

        /// <inheritdoc/>
        public IntPtr Forward2;

        /// <inheritdoc/>
        public IntPtr Inverse1;

        /// <inheritdoc/>
        public IntPtr Inverse2;
    }

    private readonly ConcurrentDictionary<int, Lazy<TwiddleCache>> _twiddleCache = new();
    private readonly object _cacheLock = new();

    static GSharpJitProvider()
    {
        // Ensure CUDA driver API is initialized before any memory allocations
        CudaDriverApi.cuInit(0);
    }

    private GSharpJitProvider()
    {
    }

    /// <inheritdoc/>
    public IntPtr AllocateMemory(int numWords)
    {
        using var scope = new JitEngine.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out var dPtr, numWords * 4));
        return dPtr;
    }


    /// <inheritdoc/>
    public void Add(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntScalarKernelsWrapper.Launch_ScalarAddKernel_Device(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    /// <inheritdoc/>
    public void Sub(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntScalarKernelsWrapper.Launch_ScalarSubKernel_Device(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    /// <inheritdoc/>
    public void Mul(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntScalarKernelsWrapper.Launch_ScalarMulKernel_Device(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    /// <inheritdoc/>
    public void BitwiseOr(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.Launch_BitwiseOrKernel_Device(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    /// <inheritdoc/>
    public void BitwiseXor(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.Launch_BitwiseXorKernel_Device(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    /// <inheritdoc/>
    public void BitwiseNot(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.Launch_BitwiseNotKernel_Device(
            leftOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    /// <inheritdoc/>
    public void Gcd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        var numWords = leftOperand.NumWords;
        using var a = new GpuBigInteger(context, numWords);
        using var b = new GpuBigInteger(context, numWords);
        context.Provider.CopyDeviceToDevice(a.DevicePointer, leftOperand.DevicePointer, numWords);
        context.Provider.CopyDeviceToDevice(b.DevicePointer, rightOperand.DevicePointer, numWords);

        if (a.CompareTo(b) < 0)
        {
            using var t = context.RentTemp(numWords);
            context.Provider.CopyDeviceToDevice(t.DevicePointer, a.DevicePointer, numWords);
            context.Provider.CopyDeviceToDevice(a.DevicePointer, b.DevicePointer, numWords);
            context.Provider.CopyDeviceToDevice(b.DevicePointer, t.DevicePointer, numWords);
        }

        using var r = new GpuBigInteger(context, numWords);
        using var newA = new GpuBigInteger(context, numWords);
        using var newB = new GpuBigInteger(context, numWords);

        while (!b.IsZero())
        {
            var bitsA = a.GetBitLength();
            var bitsB = b.GetBitLength();

            if (bitsA - bitsB > 64 || bitsA <= 64)
            {
                GpuBigIntDivRem.Mod(a, b, r);
                context.Provider.CopyDeviceToDevice(a.DevicePointer, b.DevicePointer, numWords);
                context.Provider.CopyDeviceToDevice(b.DevicePointer, r.DevicePointer, numWords);
                continue;
            }

            var shift = bitsA - 64;
            if (shift < 0)
            {
                shift = 0;
            }

            var x = Extract64Host(context, a, shift);
            var y = Extract64Host(context, b, shift);

            var progress = GpuBigIntGcdKernels.ComputeLehmerMatrix(
                x, y, out long u0, out long v0, out long u1, out long v1);

            if (!progress)
            {
                GpuBigIntDivRem.Mod(a, b, r);
                context.Provider.CopyDeviceToDevice(a.DevicePointer, b.DevicePointer, numWords);
                context.Provider.CopyDeviceToDevice(b.DevicePointer, r.DevicePointer, numWords);
            }
            else
            {
                GpuBigIntGcdKernelsWrapper.Launch_ApplyMatrix2x2Kernel_Device(
                    a.DevicePointer, b.DevicePointer, newA.DevicePointer, newB.DevicePointer, u0, v0, u1, v1, numWords,
                    gridDimX: 1, blockDimX: 1);

                if (newA.IsNegative || newB.IsNegative || newB.CompareTo(newA) > 0)
                {
                    GpuBigIntDivRem.Mod(a, b, r);
                    context.Provider.CopyDeviceToDevice(a.DevicePointer, b.DevicePointer, numWords);
                    context.Provider.CopyDeviceToDevice(b.DevicePointer, r.DevicePointer, numWords);
                }
                else
                {
                    context.Provider.CopyDeviceToDevice(a.DevicePointer, newA.DevicePointer, numWords);
                    context.Provider.CopyDeviceToDevice(b.DevicePointer, newB.DevicePointer, numWords);
                }
            }
        }

        context.Provider.CopyDeviceToDevice(resultBuffer.DevicePointer, a.DevicePointer, numWords);
    }

    private ulong Extract64Host(GpuContext context, GpuBigInteger operand, int shift)
    {
        var numWords = operand.NumWords;
        var wordShift = shift / 32;
        var bitShift = shift % 32;

        if (wordShift >= numWords)
        {
            return 0;
        }

        int wordsToRead = System.Math.Min(3, numWords - wordShift);
        var hostData = new uint[3];
        context.Provider.CopyDeviceToHost(hostData, IntPtr.Add(operand.DevicePointer, wordShift * 4), wordsToRead);

        ulong val64 = hostData[0] | ((ulong)hostData[1] << 32);
        if (bitShift > 0)
        {
            return (val64 >> bitShift) | ((ulong)hostData[2] << (64 - bitShift));
        }

        return val64;
    }

    /// <inheritdoc/>
    public bool IsZero(GpuContext context, GpuBigInteger leftOperand)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);
        using var resultBuf = context.RentTemp(1);
        using var scope_memset1 = new JitEngine.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(resultBuf.DevicePointer, 0, 4));

        GpuBigIntCmpKernelsWrapper.Launch_IsZeroKernel_Device(
            leftOperand.DevicePointer, resultBuf.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);

        var res = new uint[1];
        CopyDeviceToHost(res, resultBuf.DevicePointer, 1);
        return res[0] == 0;
    }

    /// <inheritdoc/>
    public bool IsOne(GpuContext context, GpuBigInteger leftOperand)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);
        using var resultBuf = context.RentTemp(1);
        using var scope_memset2 = new JitEngine.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(resultBuf.DevicePointer, 0, 4));

        GpuBigIntCmpKernelsWrapper.Launch_IsOneKernel_Device(
            leftOperand.DevicePointer, resultBuf.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);

        var res = new uint[1];
        CopyDeviceToHost(res, resultBuf.DevicePointer, 1);
        return res[0] == 0;
    }

    /// <inheritdoc/>
    public int CompareTo(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand)
    {
        var numWords = System.Math.Max(leftOperand.NumWords, rightOperand.NumWords);
        var dims = CalculateGridDim(numWords);

        GpuBigInteger? leftPad = null;
        GpuBigInteger? rightPad = null;
        if (leftOperand.NumWords < numWords)
        {
            leftPad = leftOperand.PadTo(numWords);
        }

        if (rightOperand.NumWords < numWords)
        {
            rightPad = rightOperand.PadTo(numWords);
        }

        try
        {
            var leftPtr = leftPad?.DevicePointer ?? leftOperand.DevicePointer;
            var rightPtr = rightPad?.DevicePointer ?? rightOperand.DevicePointer;

            // Rent 2 words since the result is a ulong (8 bytes)
            using var resultBuf = context.RentTemp(2);
            using var scopeMemset3 = new JitEngine.CudaContextScope();
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(resultBuf.DevicePointer, 0, 8));

            GpuBigIntCmpKernelsWrapper.Launch_CompareToKernel_Device(
                leftPtr, rightPtr, resultBuf.DevicePointer, numWords,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize);

            var res = new ulong[1];
            var handle = GCHandle.Alloc(res, GCHandleType.Pinned);
            try
            {
                using var scope = new JitEngine.CudaContextScope();
                CudaException.ThrowIfError(
                    CudaDriverApi.cuMemcpyDtoH_v2(handle.AddrOfPinnedObject(), resultBuf.DevicePointer, 8));
            }
            finally
            {
                handle.Free();
            }

            var val = res[0];
            if (val == 0)
            {
                return 0;
            }

            var cmp = val & 0xFFFFFFFFul;
            return cmp == 2 ? 1 : -1;
        }
        finally
        {
            leftPad?.Dispose();
            rightPad?.Dispose();
        }
    }


    /// <inheritdoc/>
    public int GetBitLength(GpuContext context, GpuBigInteger leftOperand)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);
        using var resultBuf = context.RentTemp(1);
        using var scopeMemset4 = new JitEngine.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(resultBuf.DevicePointer, 0, 4));

        GpuBigIntCmpKernelsWrapper.Launch_GetBitLengthKernel_Device(
            leftOperand.DevicePointer, resultBuf.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);

        var res = new uint[1];
        CopyDeviceToHost(res, resultBuf.DevicePointer, 1);
        return (int)res[0];
    }

    /// <inheritdoc/>
    public void FreeMemory(IntPtr devicePointer)
    {
        if (devicePointer != IntPtr.Zero)
        {
            using var scope = new JitEngine.CudaContextScope();
            CudaDriverApi.cuMemFree_v2(devicePointer);
        }
    }

    /// <inheritdoc/>
    public void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords)
    {
        var handle = GCHandle.Alloc(hostData, GCHandleType.Pinned);
        try
        {
            using var scope = new JitEngine.CudaContextScope();
            CudaException.ThrowIfError(
                CudaDriverApi.cuMemcpyHtoD_v2(devicePointer, handle.AddrOfPinnedObject(),
                    System.Math.Min(hostData.Length, numWords) * 4));
        }
        finally
        {
            handle.Free();
        }
    }

    /// <inheritdoc/>
    public void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords)
    {
        var handle = GCHandle.Alloc(hostData, GCHandleType.Pinned);
        try
        {
            using var scope = new JitEngine.CudaContextScope();
            CudaException.ThrowIfError(
                CudaDriverApi.cuMemcpyDtoH_v2(handle.AddrOfPinnedObject(), devicePointer, numWords * 4));
        }
        finally
        {
            handle.Free();
        }
    }

    /// <inheritdoc/>
    public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords)
    {
        using var scope = new JitEngine.CudaContextScope();
        CudaException.ThrowIfError(
            CudaDriverApi.cuMemcpyDtoD_v2(destPointer, srcPointer, numWords * 4));
    }

    // Dynamic CarryBuffer allocation to avoid race conditions.
    // We pool them based on size to avoid cuMemAlloc overhead on every call.
    private readonly ConcurrentDictionary<int, Lazy<ConcurrentBag<IntPtr>>> _carryBufferPool = new();

    private IntPtr RentCarryBuffer(int numChunks)
    {
        var sizeBytes = (numChunks + 1) * 8;
        if (_carryBufferPool.TryGetValue(sizeBytes, out var lazyBag) && lazyBag.IsValueCreated && lazyBag.Value.TryTake(out var ptr))
        {
            return ptr;
        }

        using (new JitEngine.CudaContextScope())
        {
            CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out ptr, sizeBytes));
        }

        return ptr;
    }

    private void ReturnCarryBuffer(int numChunks, IntPtr ptr)
    {
        var sizeBytes = (numChunks + 1) * 8;
        var bag = _carryBufferPool.GetOrAdd(sizeBytes, _ => new Lazy<ConcurrentBag<IntPtr>>(() => [])).Value;
        bag.Add(ptr);
    }

    /// <inheritdoc/>
    public void Add(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        const int ChunkSize = 256;
        var numChunks = (leftOperand.NumWords + ChunkSize - 1) / ChunkSize;
        var dims = CalculateGridDim(numChunks);

        var carryBuffer = RentCarryBuffer(numChunks);
        try
        {
            var carryBufferSize = (numChunks + 1) * 8;
            using var scope_memset5 = new JitEngine.CudaContextScope();
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(carryBuffer, 0, carryBufferSize));

            GpuBigIntKernelsWrapper.Launch_ChunkAddKernel_Device(
                leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, carryBuffer,
                leftOperand.NumWords, ChunkSize,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize);

            GpuBigIntKernelsWrapper.Launch_ApplyAddCarryKernel_Device(
                resultBuffer.DevicePointer, carryBuffer, numChunks, leftOperand.NumWords, ChunkSize,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
        }
        finally
        {
            ReturnCarryBuffer(numChunks, carryBuffer);
        }
    }

    /// <inheritdoc/>
    public void Sub(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        const int ChunkSize = 256;
        var numChunks = (leftOperand.NumWords + ChunkSize - 1) / ChunkSize;
        var dims = CalculateGridDim(numChunks);

        var carryBuffer = RentCarryBuffer(numChunks);
        try
        {
            var carryBufferSize = (numChunks + 1) * 8;
            using var scope_memset6 = new JitEngine.CudaContextScope();
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(carryBuffer, 0, carryBufferSize));

            GpuBigIntKernelsWrapper.Launch_ChunkSubKernel_Device(
                leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, carryBuffer,
                leftOperand.NumWords, ChunkSize,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize);

            GpuBigIntKernelsWrapper.Launch_ApplySubBorrowKernel_Device(
                resultBuffer.DevicePointer, carryBuffer, numChunks, leftOperand.NumWords, ChunkSize,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
        }
        finally
        {
            ReturnCarryBuffer(numChunks, carryBuffer);
        }
    }

    /// <inheritdoc/>
    public void BitwiseAnd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.Launch_BitwiseAndKernel_Device(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    /// <inheritdoc/>
    public void Mul(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        var outWords = leftOperand.NumWords + rightOperand.NumWords;
        var requiredLen = outWords * 2;
        var m = 1;
        while (m < requiredLen)
        {
            m *= 2;
        }

        var twiddle = GetOrCreateTwiddleFactors(context, m);

        using var buf1A = context.RentTemp(m);
        using var buf2A = context.RentTemp(m);
        using var buf1B = context.RentTemp(m);
        using var buf2B = context.RentTemp(m);
        using var tmpBuf = context.RentTemp(m);

        using var uncarriedLow = context.RentTemp(m);
        using var uncarriedHigh = context.RentTemp(m);

        var n2 = 1;
        while (n2 * n2 < m)
        {
            n2 *= 2;
        }

        var n1 = m / n2;
        var maxL = m <= 512 ? m : System.Math.Max(n1, n2);
        var sharedMemWords = System.Math.Max(1055, maxL);
        var threadsNeeded = maxL / 2;

        var blockDim = (uint)System.Math.Min(1024, System.Math.Max(256, threadsNeeded));
        var gridDim = (uint)((m + blockDim - 1) / blockDim);

        // Global NTT (Non-Cooperative) Launch

        // 1. Unpack
        GpuBigIntMulKernelsWrapper.Launch_Kernel_MultiplyNtt_Unpack_Device(
            leftOperand.DevicePointer, rightOperand.DevicePointer,
            buf1A.DevicePointer, buf2A.DevicePointer, buf1B.DevicePointer, buf2B.DevicePointer,
            m, leftOperand.NumWords, rightOperand.NumWords,
            gridDimX: gridDim, blockDimX: blockDim
        );

        // 2. Forward NTTs
        LaunchNttHost(buf1A.DevicePointer, tmpBuf.DevicePointer, twiddle.Forward1, m, n1, n2,
            GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, false);
        LaunchNttHost(buf2A.DevicePointer, tmpBuf.DevicePointer, twiddle.Forward2, m, n1, n2,
            GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, false);
        LaunchNttHost(buf1B.DevicePointer, tmpBuf.DevicePointer, twiddle.Forward1, m, n1, n2,
            GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, false);
        LaunchNttHost(buf2B.DevicePointer, tmpBuf.DevicePointer, twiddle.Forward2, m, n1, n2,
            GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, false);

        // 3. Pointwise Multiply
        GpuBigIntMulKernelsWrapper.Launch_Kernel_MultiplyNtt_PointwiseMultiply_Device(
            buf1A.DevicePointer, buf2A.DevicePointer, buf1B.DevicePointer, buf2B.DevicePointer, m,
            gridDimX: gridDim, blockDimX: blockDim
        );

        // 4. Inverse NTTs
        LaunchNttHost(buf1A.DevicePointer, tmpBuf.DevicePointer, twiddle.Inverse1, m, n1, n2,
            GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, true);
        LaunchNttHost(buf2A.DevicePointer, tmpBuf.DevicePointer, twiddle.Inverse2, m, n1, n2,
            GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, true);

        // 5. CRT Coefficients
        GpuBigIntMulKernelsWrapper.Launch_Kernel_MultiplyNtt_ComputeCRT_Device(
            buf1A.DevicePointer, buf2A.DevicePointer, uncarriedLow.DevicePointer, uncarriedHigh.DevicePointer, m,
            gridDimX: gridDim, blockDimX: blockDim
        );

        // 6. Resolve Carries (Cooperative Block)
        int chunkSize = 1024;
        int numBlocks = (outWords + chunkSize - 1) / chunkSize;
        using var blockCarries = context.RentTemp(numBlocks);

        GpuBigIntNttKernelsWrapper.Launch_Kernel_ResolveNttCarries_Blocked_Device(
            uncarriedLow.DevicePointer, uncarriedHigh.DevicePointer, resultBuffer.DevicePointer,
            blockCarries.DevicePointer, m, outWords, chunkSize,
            gridDimX: (uint)numBlocks, blockDimX: (uint)chunkSize, sharedMemBytes: (uint)(chunkSize * 8)
        );

        var applyBlockDim = (uint)System.Math.Min(1024, System.Math.Max(256, numBlocks));
        var applyGridDim = (uint)((numBlocks + applyBlockDim - 1) / applyBlockDim);

        GpuBigIntNttKernelsWrapper.Launch_Kernel_ResolveNttCarries_Apply_Device(
            resultBuffer.DevicePointer, blockCarries.DevicePointer, outWords, chunkSize, numBlocks,
            gridDimX: applyGridDim, blockDimX: applyBlockDim
        );
    }

    private void LaunchNttHost(IntPtr buffer, IntPtr tempBuffer, IntPtr twiddle, int m, int n1, int n2, uint p,
        ulong pm, bool isInverse)
    {
        if (m <= 512)
        {
            var threads = m / 2;
            GpuBigIntMulKernelsWrapper.Launch_Kernel_MultiplyNtt_SharedRow_Device(
                buffer, tempBuffer, twiddle, m, n1, n2, p, pm, false, false, 1, m,
                gridDimX: 1, blockDimX: (uint)threads, sharedMemBytes: (uint)((m + (m >> 5)) * 4)
            );
            return;
        }

        var firstW = isInverse ? n1 : n2;
        var firstH = isInverse ? n2 : n1;
        var secondW = isInverse ? n2 : n1;
        var secondH = isInverse ? n1 : n2;
        
        var currentBuf = buffer;
        
        if (!isInverse)
        {
            LaunchTranspose(currentBuf, tempBuffer, firstW, firstH);
            currentBuf = tempBuffer;
        }

        LaunchSharedRow(currentBuf, tempBuffer, twiddle, m, n1, n2, p, pm, false, true, firstW, firstH);
        
        var nextBuf = currentBuf == buffer ? tempBuffer : buffer;
        LaunchTranspose(currentBuf, nextBuf, firstH, firstW);
        currentBuf = nextBuf;
        
        LaunchSharedRow(currentBuf, tempBuffer, twiddle, m, n1, n2, p, pm, false, false, secondW, secondH);
        
        if (isInverse)
        {
            LaunchTranspose(currentBuf, buffer, secondH, secondW);
        }
    }

    private void LaunchSharedRow(IntPtr buffer, IntPtr tempBuffer, IntPtr twiddle, int m, int n1, int n2, uint p,
        ulong pm, bool applyTwiddleBefore, bool applyTwiddleAfter, int maxBlocks, int currentL)
    {
        var threadsNeeded = currentL / 2;
        var blockDim = (uint)System.Math.Min(1024, System.Math.Max(256, threadsNeeded));
        var gridDim = (uint)System.Math.Min(65535, maxBlocks);

        GpuBigIntMulKernelsWrapper.Launch_Kernel_MultiplyNtt_SharedRow_Device(
            buffer, tempBuffer, twiddle, m, n1, n2, p, pm, applyTwiddleBefore, applyTwiddleAfter, maxBlocks, currentL,
            gridDimX: gridDim, blockDimX: blockDim,
            sharedMemBytes: (uint)(System.Math.Max(1055, currentL + (currentL >> 5)) * 4)
        );
    }

    private void LaunchTranspose(IntPtr input, IntPtr output, int width, int height)
    {
        uint gridDimX = (uint)((width + 31) / 32);
        uint gridDimY = (uint)((height + 31) / 32);
        uint totalGrid = gridDimX * gridDimY;
        GpuBigIntMulKernelsWrapper.Launch_Kernel_MultiplyNtt_Transpose_Device(
            input, output, width, height,
            gridDimX: totalGrid, gridDimY: 1, blockDimX: 256, blockDimY: 1,
            sharedMemBytes: 32 * 33 * 4);
    }

    private uint CpuModPow(ulong baseVal, ulong exp, uint p)
    {
        ulong res = 1;
        baseVal %= p;
        while (exp > 0)
        {
            if ((exp & 1) != 0)
            {
                res = (res * baseVal) % p;
            }

            baseVal = (baseVal * baseVal) % p;
            exp >>= 1;
        }

        return (uint)res;
    }

    private TwiddleCache GetOrCreateTwiddleFactors(GpuContext context, int m)
    {
        return _twiddleCache.GetOrAdd(m, key => new Lazy<TwiddleCache>(() =>
        {
            var p1 = GpuBigIntNttKernels.Prime1;
            var p2 = GpuBigIntNttKernels.Prime2;
            var g1 = GpuBigIntNttKernels.NttG1;
            var g2 = GpuBigIntNttKernels.NttG2;

            var w1 = CpuModPow(g1, (p1 - 1) / (uint)key, p1);
            var w2 = CpuModPow(g2, (p2 - 1) / (uint)key, p2);

            var iw1 = CpuModPow(w1, p1 - 2, p1);
            var iw2 = CpuModPow(w2, p2 - 2, p2);

            ulong currentWeight1 = 1, currentInverseWeight1 = 1;
            ulong currentWeight2 = 1, currentInverseWeight2 = 1;

            var f1 = new uint[key];
            var f2 = new uint[key];
            var i1 = new uint[key];
            var i2 = new uint[key];

            for (var i = 0; i < key; i++)
            {
                f1[i] = (uint)currentWeight1;
                i1[i] = (uint)currentInverseWeight1;
                f2[i] = (uint)currentWeight2;
                i2[i] = (uint)currentInverseWeight2;

                currentWeight1 = (currentWeight1 * w1) % p1;
                currentInverseWeight1 = (currentInverseWeight1 * iw1) % p1;
                currentWeight2 = (currentWeight2 * w2) % p2;
                currentInverseWeight2 = (currentInverseWeight2 * iw2) % p2;
            }

            var dF1 = context.Provider.AllocateMemory(key);
            var dF2 = context.Provider.AllocateMemory(key);
            var dI1 = context.Provider.AllocateMemory(key);
            var dI2 = context.Provider.AllocateMemory(key);

            context.Provider.CopyHostToDevice(dF1, f1, key);
            context.Provider.CopyHostToDevice(dF2, f2, key);
            context.Provider.CopyHostToDevice(dI1, i1, key);
            context.Provider.CopyHostToDevice(dI2, i2, key);

            return new TwiddleCache
            {
                Forward1 = dF1,
                Forward2 = dF2,
                Inverse1 = dI1,
                Inverse2 = dI2
            };
        })).Value;
    }

    /// <inheritdoc/>
    public void ShiftLeft(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.Launch_ShiftLeftKernel_Device(
            leftOperand.DevicePointer, wordShift, bitShift, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    /// <inheritdoc/>
    public void ShiftRight(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.Launch_ShiftRightKernel_Device(
            leftOperand.DevicePointer, wordShift, bitShift, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    /// <inheritdoc/>
    public bool Square(GpuContext context, IntPtr devicePointer, int logicalWords)
    {
        using var tempA = new GpuBigInteger(context, logicalWords);
        context.Provider.CopyDeviceToDevice(tempA.DevicePointer, devicePointer, logicalWords);
        using var tempResult = new GpuBigInteger(context, logicalWords * 2);
        GpuBigIntMath.Mul(tempA, tempA, tempResult);
        context.Provider.CopyDeviceToDevice(devicePointer, tempResult.DevicePointer, logicalWords * 2);
        return true;
    }

    private static (uint GridSize, uint BlockSize) CalculateGridDim(int elements, uint blockSize = 256)
    {
        var gridSize = (uint)((elements + blockSize - 1) / blockSize);
        if (gridSize == 0)
        {
            gridSize = 1;
        }

        return (gridSize, blockSize);
    }

}






