using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Interop;
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
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntScalarKernelsWrapper.Launch_ScalarAddKernel_Device(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    public void Sub(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntScalarKernelsWrapper.Launch_ScalarSubKernel_Device(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    public void Mul(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntScalarKernelsWrapper.Launch_ScalarMulKernel_Device(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    public void BitwiseOr(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.Launch_BitwiseOrKernel_Device(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    public void BitwiseXor(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.Launch_BitwiseXorKernel_Device(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    public void BitwiseNot(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.Launch_BitwiseNotKernel_Device(
            leftOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    public void Gcd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        var numWords = leftOperand.NumWords;
        using var tempA = new GpuBigInteger(context, numWords);
        using var tempB = new GpuBigInteger(context, numWords);
        context.Provider.CopyDeviceToDevice(tempA.DevicePointer, leftOperand.DevicePointer, numWords);
        context.Provider.CopyDeviceToDevice(tempB.DevicePointer, rightOperand.DevicePointer, numWords);

        GpuBigIntGcdKernelsWrapper.Launch_Kernel_Gcd_Device(
            tempA.DevicePointer, tempB.DevicePointer, resultBuffer.DevicePointer, numWords,
            gridDimX: 1, blockDimX: 1);
    }

    public bool IsZero(GpuContext context, GpuBigInteger leftOperand)
    {
        var words = new uint[leftOperand.NumWords];
        CopyDeviceToHost(words, leftOperand.DevicePointer, leftOperand.NumWords);
        foreach (var word in words)
        {
            if (word != 0)
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

        for (var i = 1; i < words.Length; i++)
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
        for (var i = maxWords - 1; i >= 0; i--)
        {
            var l = i < wordsLeft.Length ? wordsLeft[i] : 0;
            var r = i < wordsRight.Length ? wordsRight[i] : 0;
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
        for (var i = words.Length - 1; i >= 0; i--)
        {
            if (words[i] == 0)
            {
                continue;
            }

            var w = words[i];
            var bitLen = 0;
            while (w > 0)
            {
                bitLen++;
                w >>= 1;
            }

            return i * 32 + bitLen;
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
        const int chunkSize = 256;
        var numChunks = (leftOperand.NumWords + chunkSize - 1) / chunkSize;
        var dims = CalculateGridDim(numChunks);

        var carryBuffer = RentCarryBuffer(numChunks);
        try
        {
            var carryBufferSize = (numChunks + 1) * 8;
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(carryBuffer, 0, carryBufferSize));

            GpuBigIntKernelsWrapper.Launch_ChunkAddKernel_Device(
                leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, carryBuffer,
                leftOperand.NumWords, chunkSize,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize);

            GpuBigIntKernelsWrapper.Launch_ApplyAddCarryKernel_Device(
                resultBuffer.DevicePointer, carryBuffer, numChunks, leftOperand.NumWords, chunkSize,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
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
        var dims = CalculateGridDim(numChunks);

        var carryBuffer = RentCarryBuffer(numChunks);
        try
        {
            var carryBufferSize = (numChunks + 1) * 8;
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(carryBuffer, 0, carryBufferSize));

            GpuBigIntKernelsWrapper.Launch_ChunkSubKernel_Device(
                leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, carryBuffer,
                leftOperand.NumWords, chunkSize,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize);

            GpuBigIntKernelsWrapper.Launch_ApplySubBorrowKernel_Device(
                resultBuffer.DevicePointer, carryBuffer, numChunks, leftOperand.NumWords, chunkSize,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
        }
        finally
        {
            ReturnCarryBuffer(numChunks, carryBuffer);
        }
    }

    public void BitwiseAnd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.Launch_BitwiseAndKernel_Device(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

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

        var buf1A = context.Provider.AllocateMemory(m);
        var buf2A = context.Provider.AllocateMemory(m);
        var buf1B = context.Provider.AllocateMemory(m);
        var buf2B = context.Provider.AllocateMemory(m);
        var tmpBuf = context.Provider.AllocateMemory(m);
        
        var uncarriedLow = context.Provider.AllocateMemory(m);
        var uncarriedHigh = context.Provider.AllocateMemory(m);

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

        GpuBigIntMulKernelsWrapper.Launch_Kernel_MultiplyNtt_Device(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer,
            buf1A, buf2A, buf1B, buf2B, tmpBuf, uncarriedLow, uncarriedHigh,
            twiddle.Forward1, twiddle.Forward2, twiddle.Inverse1, twiddle.Inverse2,
            leftOperand.NumWords, rightOperand.NumWords, outWords, m,
            gridDimX: gridDim, blockDimX: blockDim,
            sharedMemBytes: (uint)(sharedMemWords * 4)
        );

        GpuBigIntNttKernelsWrapper.Launch_Kernel_ResolveNttCarries_SingleThread_Device(
            uncarriedLow, uncarriedHigh, resultBuffer.DevicePointer, m, outWords,
            gridDimX: 1, blockDimX: 1
        );

        context.Provider.FreeMemory(buf1A);
        context.Provider.FreeMemory(buf2A);
        context.Provider.FreeMemory(buf1B);
        context.Provider.FreeMemory(buf2B);
        context.Provider.FreeMemory(tmpBuf);
        context.Provider.FreeMemory(uncarriedLow);
        context.Provider.FreeMemory(uncarriedHigh);
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
        });
    }

    public void ShiftLeft(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.Launch_ShiftLeftKernel_Device(
            leftOperand.DevicePointer, wordShift, bitShift, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
    }

    public void ShiftRight(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.Launch_ShiftRightKernel_Device(
            leftOperand.DevicePointer, wordShift, bitShift, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize);
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

