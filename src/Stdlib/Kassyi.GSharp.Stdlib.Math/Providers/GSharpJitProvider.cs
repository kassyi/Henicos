using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Runtime.Jit;
using System.Collections.Concurrent;
using Kassyi.GSharp.Stdlib.Math.Kernels;

namespace Kassyi.GSharp.Stdlib.Math.Providers;

public sealed class GSharpJitProvider : IGpuExecutionProvider
{
    public static readonly GSharpJitProvider Instance = new();

    private class TwiddleCache
    {
        public IntPtr Forward1;
        public IntPtr Forward2;
        public IntPtr Inverse1;
        public IntPtr Inverse2;
    }

    private readonly ConcurrentDictionary<int, TwiddleCache> _twiddleCache = new();

    private GSharpJitProvider()
    {
    }

    public IntPtr AllocateMemory(int numWords)
    {
        CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out var dPtr, numWords * 4));
        return dPtr;
    }


    public void Add(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer)
    {
        uint blockDim = 256;
        var gridDim = (uint)((leftOperand.NumWords + blockDim - 1) / blockDim);
        if (gridDim == 0)
        {
            gridDim = 1;
        }

        GpuBigIntScalarKernelsWrapper.Launch_ScalarAddKernel_Device(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: gridDim, blockDimX: blockDim);
    }

    public void Sub(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer)
    {
        uint blockDim = 256;
        var gridDim = (uint)((leftOperand.NumWords + blockDim - 1) / blockDim);
        if (gridDim == 0)
        {
            gridDim = 1;
        }

        GpuBigIntScalarKernelsWrapper.Launch_ScalarSubKernel_Device(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: gridDim, blockDimX: blockDim);
    }

    public void Mul(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer)
    {
        uint blockDim = 256;
        var gridDim = (uint)((leftOperand.NumWords + blockDim - 1) / blockDim);
        if (gridDim == 0)
        {
            gridDim = 1;
        }

        GpuBigIntScalarKernelsWrapper.Launch_ScalarMulKernel_Device(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: gridDim, blockDimX: blockDim);
    }

    public void BitwiseOr(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        uint blockDim = 256;
        var gridDim = (uint)((leftOperand.NumWords + blockDim - 1) / blockDim);
        if (gridDim == 0)
        {
            gridDim = 1;
        }

        GpuBigIntKernelsWrapper.Launch_BitwiseOrKernel_Device(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: gridDim, blockDimX: blockDim);
    }

    public void BitwiseXor(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        uint blockDim = 256;
        var gridDim = (uint)((leftOperand.NumWords + blockDim - 1) / blockDim);
        if (gridDim == 0)
        {
            gridDim = 1;
        }

        GpuBigIntKernelsWrapper.Launch_BitwiseXorKernel_Device(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: gridDim, blockDimX: blockDim);
    }

    public void BitwiseNot(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger resultBuffer)
    {
        uint blockDim = 256;
        var gridDim = (uint)((leftOperand.NumWords + blockDim - 1) / blockDim);
        if (gridDim == 0)
        {
            gridDim = 1;
        }

        GpuBigIntKernelsWrapper.Launch_BitwiseNotKernel_Device(
            leftOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: gridDim, blockDimX: blockDim);
    }

    public void Gcd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        // Simple fallback to CPU for GCD since it's an iterative CPU-GPU hybrid algorithm
        var leftWords = new uint[leftOperand.NumWords];
        var rightWords = new uint[rightOperand.NumWords];
        CopyDeviceToHost(leftWords, leftOperand.DevicePointer, leftOperand.NumWords);
        CopyDeviceToHost(rightWords, rightOperand.DevicePointer, rightOperand.NumWords);

        var leftBig = new System.Numerics.BigInteger(MemoryMarshal.Cast<uint, byte>(leftWords).ToArray(),
            isUnsigned: true, isBigEndian: false);
        var rightBig = new System.Numerics.BigInteger(MemoryMarshal.Cast<uint, byte>(rightWords).ToArray(),
            isUnsigned: true, isBigEndian: false);

        var resBig = System.Numerics.BigInteger.GreatestCommonDivisor(leftBig, rightBig);
        var resBytes = resBig.ToByteArray(isUnsigned: true, isBigEndian: false);
        var resWords = new uint[resultBuffer.NumWords];
        Buffer.BlockCopy(resBytes, 0, resWords, 0, System.Math.Min(resBytes.Length, resWords.Length * 4));
        CopyHostToDevice(resultBuffer.DevicePointer, resWords, resultBuffer.NumWords);
    }

    public bool IsZero(GpuContext context, GpuBigInteger leftOperand)
    {
        var words = new uint[leftOperand.NumWords];
        CopyDeviceToHost(words, leftOperand.DevicePointer, leftOperand.NumWords);
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    public bool IsOne(GpuContext context, GpuBigInteger leftOperand)
    {
        var words = new uint[leftOperand.NumWords];
        CopyDeviceToHost(words, leftOperand.DevicePointer, leftOperand.NumWords);
        if (words.Length == 0)
        {
            return false;
        }

        if (words[0] != 1)
        {
            return false;
        }

        for (int i = 1; i < words.Length; i++)
        {
            if (words[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    public int CompareTo(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand)
    {
        var wordsLeft = new uint[leftOperand.NumWords];
        var wordsRight = new uint[rightOperand.NumWords];
        CopyDeviceToHost(wordsLeft, leftOperand.DevicePointer, leftOperand.NumWords);
        CopyDeviceToHost(wordsRight, rightOperand.DevicePointer, rightOperand.NumWords);

        var maxWords = System.Math.Max(wordsLeft.Length, wordsRight.Length);
        for (int i = maxWords - 1; i >= 0; i--)
        {
            uint l = i < wordsLeft.Length ? wordsLeft[i] : 0;
            uint r = i < wordsRight.Length ? wordsRight[i] : 0;
            if (l > r)
            {
                return 1;
            }

            if (l < r)
            {
                return -1;
            }
        }

        return 0;
    }


    public int GetBitLength(GpuContext context, GpuBigInteger leftOperand)
    {
        var words = new uint[leftOperand.NumWords];
        CopyDeviceToHost(words, leftOperand.DevicePointer, leftOperand.NumWords);
        for (int i = words.Length - 1; i >= 0; i--)
        {
            if (words[i] != 0)
            {
                var w = words[i];
                int bitLen = 0;
                while (w > 0)
                {
                    bitLen++;
                    w >>= 1;
                }

                return i * 32 + bitLen;
            }
        }

        return 0;
    }

    public void FreeMemory(IntPtr devicePointer)
    {
        if (devicePointer != IntPtr.Zero)
        {
            CudaDriverApi.cuMemFree_v2(devicePointer);
        }
    }

    public void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords)
    {
        var handle = GCHandle.Alloc(hostData, GCHandleType.Pinned);
        try
        {
            CudaException.ThrowIfError(
                CudaDriverApi.cuMemcpyHtoD_v2(devicePointer, handle.AddrOfPinnedObject(), hostData.Length * 4));
        }
        finally
        {
            handle.Free();
        }
    }

    public void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords)
    {
        var handle = GCHandle.Alloc(hostData, GCHandleType.Pinned);
        try
        {
            CudaException.ThrowIfError(
                CudaDriverApi.cuMemcpyDtoH_v2(handle.AddrOfPinnedObject(), devicePointer, numWords * 4));
        }
        finally
        {
            handle.Free();
        }
    }

    public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords)
    {
        CudaException.ThrowIfError(
            CudaDriverApi.cuMemcpyDtoD_v2(destPointer, srcPointer, numWords * 4));
    }

    // Dynamic CarryBuffer allocation to avoid race conditions.
    // We pool them based on size to avoid cuMemAlloc overhead on every call.
    private readonly ConcurrentDictionary<int, ConcurrentBag<IntPtr>> _carryBufferPool = new();

    private IntPtr RentCarryBuffer(int numChunks)
    {
        var sizeBytes = (numChunks + 1) * 8;
        if (_carryBufferPool.TryGetValue(sizeBytes, out var bag) && bag.TryTake(out var ptr))
        {
            return ptr;
        }

        CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out ptr, sizeBytes));
        return ptr;
    }

    private void ReturnCarryBuffer(int numChunks, IntPtr ptr)
    {
        var sizeBytes = (numChunks + 1) * 8;
        var bag = _carryBufferPool.GetOrAdd(sizeBytes, _ => []);
        bag.Add(ptr);
    }

    public void Add(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        var chunkSize = 256;
        var numChunks = (leftOperand.NumWords + chunkSize - 1) / chunkSize;
        uint blockSize = 256;
        var gridSize = (numChunks + blockSize - 1) / blockSize;
        if (gridSize == 0)
        {
            gridSize = 1;
        }

        var carryBuffer = RentCarryBuffer(numChunks);
        try
        {
            var carryBufferSize = (numChunks + 1) * 8;
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(carryBuffer, 0, carryBufferSize));

            GpuBigIntKernelsWrapper.Launch_ChunkAddKernel_Device(
                leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, carryBuffer,
                leftOperand.NumWords, chunkSize,
                gridDimX: (uint)gridSize, blockDimX: blockSize);

            GpuBigIntKernelsWrapper.Launch_ApplyAddCarryKernel_Device(
                resultBuffer.DevicePointer, carryBuffer, numChunks, leftOperand.NumWords, chunkSize,
                gridDimX: (uint)gridSize, blockDimX: blockSize);
        }
        finally
        {
            ReturnCarryBuffer(numChunks, carryBuffer);
        }
    }

    public void Sub(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        var chunkSize = 256;
        var numChunks = (leftOperand.NumWords + chunkSize - 1) / chunkSize;
        uint blockSize = 256;
        var gridSize = (numChunks + blockSize - 1) / blockSize;
        if (gridSize == 0)
        {
            gridSize = 1;
        }

        var carryBuffer = RentCarryBuffer(numChunks);
        try
        {
            var carryBufferSize = (numChunks + 1) * 8;
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(carryBuffer, 0, carryBufferSize));

            GpuBigIntKernelsWrapper.Launch_ChunkSubKernel_Device(
                leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, carryBuffer,
                leftOperand.NumWords, chunkSize,
                gridDimX: (uint)gridSize, blockDimX: blockSize);

            GpuBigIntKernelsWrapper.Launch_ApplySubBorrowKernel_Device(
                resultBuffer.DevicePointer, carryBuffer, numChunks, leftOperand.NumWords, chunkSize,
                gridDimX: (uint)gridSize, blockDimX: blockSize);
        }
        finally
        {
            ReturnCarryBuffer(numChunks, carryBuffer);
        }
    }

    public void BitwiseAnd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        const uint BlockSize = 256;
        var gridSize = (leftOperand.NumWords + BlockSize - 1) / BlockSize;
        if (gridSize == 0)
        {
            gridSize = 1;
        }

        GpuBigIntKernelsWrapper.Launch_BitwiseAndKernel_Device(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: (uint)gridSize, blockDimX: BlockSize);
    }

    public void Mul(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        var outWords = leftOperand.NumWords + rightOperand.NumWords;
        var requiredLen = outWords * 2;
        int m = 1;
        while (m < requiredLen)
        {
            m *= 2;
        }

        var twiddle = GetOrCreateTwiddleFactors(context, m);

        var buf1A = context.Provider.AllocateMemory(m);
        var buf2A = context.Provider.AllocateMemory(m);
        var buf1B = context.Provider.AllocateMemory(m);
        var buf2B = context.Provider.AllocateMemory(m);
        var tmpBuf = context.Provider.AllocateMemory(m);

        var gridDim = (uint)((m + 255) / 256);
        uint blockDim = 256;

        int n2 = 1;
        while (n2 * n2 < m)
        {
            n2 *= 2;
        }

        var n1 = m / n2;
        var maxL = m <= 512 ? m : System.Math.Max(n1, n2);
        var sharedMemWords = System.Math.Max(1055, maxL);

        GpuBigIntMulKernelsWrapper.Launch_Kernel_MultiplyNtt_Device(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer,
            buf1A, buf2A, buf1B, buf2B, tmpBuf,
            twiddle.Forward1, twiddle.Forward2, twiddle.Inverse1, twiddle.Inverse2,
            leftOperand.NumWords, rightOperand.NumWords, outWords, m,
            gridDimX: gridDim, blockDimX: blockDim,
            sharedMemBytes: (uint)(sharedMemWords * 4)
        );

        context.Provider.FreeMemory(buf1A);
        context.Provider.FreeMemory(buf2A);
        context.Provider.FreeMemory(buf1B);
        context.Provider.FreeMemory(buf2B);
        context.Provider.FreeMemory(tmpBuf);
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
        return _twiddleCache.GetOrAdd(m, key =>
        {
            var p1 = GpuBigIntNttKernels.NttP1;
            var p2 = GpuBigIntNttKernels.NttP2;
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
        });
    }

    public void ShiftLeft(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        uint blockSize = 256;
        var gridSize = (leftOperand.NumWords + blockSize - 1) / blockSize;
        if (gridSize == 0)
        {
            gridSize = 1;
        }

        GpuBigIntKernelsWrapper.Launch_ShiftLeftKernel_Device(
            leftOperand.DevicePointer, wordShift, bitShift, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: (uint)gridSize, blockDimX: blockSize);
    }

    public void ShiftRight(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        uint blockSize = 256;
        var gridSize = (leftOperand.NumWords + blockSize - 1) / blockSize;
        if (gridSize == 0)
        {
            gridSize = 1;
        }

        GpuBigIntKernelsWrapper.Launch_ShiftRightKernel_Device(
            leftOperand.DevicePointer, wordShift, bitShift, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: (uint)gridSize, blockDimX: blockSize);
    }

    public bool Square(GpuContext context, IntPtr devicePointer, int logicalWords)
    {
        using var tempA = new GpuBigInteger(context, logicalWords);
        context.Provider.CopyDeviceToDevice(tempA.DevicePointer, devicePointer, logicalWords);
        using var tempResult = new GpuBigInteger(context, logicalWords * 2);
        GpuBigInteger.Mul(tempA, tempA, tempResult);
        context.Provider.CopyDeviceToDevice(devicePointer, tempResult.DevicePointer, logicalWords * 2);
        return true;
    }
}

