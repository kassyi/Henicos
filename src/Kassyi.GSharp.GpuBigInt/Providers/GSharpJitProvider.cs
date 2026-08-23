using System.Runtime.InteropServices;
using Kassyi.GSharp.Runtime.Interop;
using System.Collections.Concurrent;
using Kassyi.GSharp.GpuBigInt.Kernels;

namespace Kassyi.GSharp.GpuBigInt.Providers;

public sealed class GSharpJitProvider : IGpuExecutionProvider
{
    public static readonly GSharpJitProvider Instance = new();

    private readonly ConcurrentDictionary<string, GpuContext> _tempPool = new();

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

    public void Add(GpuContext context, GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        var chunkSize = 256;
        var numChunks = (a.NumWords + chunkSize - 1) / chunkSize;
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
                a.DevicePointer, b.DevicePointer, result.DevicePointer, carryBuffer, a.NumWords, chunkSize,
                gridDimX: (uint)gridSize, blockDimX: blockSize);

            GpuBigIntKernelsWrapper.Launch_ApplyAddCarryKernel_Device(
                result.DevicePointer, carryBuffer, numChunks, a.NumWords, chunkSize,
                gridDimX: (uint)gridSize, blockDimX: blockSize);
        }
        finally
        {
            ReturnCarryBuffer(numChunks, carryBuffer);
        }
    }

    public void Sub(GpuContext context, GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        var chunkSize = 256;
        var numChunks = (a.NumWords + chunkSize - 1) / chunkSize;
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
                a.DevicePointer, b.DevicePointer, result.DevicePointer, carryBuffer, a.NumWords, chunkSize,
                gridDimX: (uint)gridSize, blockDimX: blockSize);

            GpuBigIntKernelsWrapper.Launch_ApplySubBorrowKernel_Device(
                result.DevicePointer, carryBuffer, numChunks, a.NumWords, chunkSize,
                gridDimX: (uint)gridSize, blockDimX: blockSize);
        }
        finally
        {
            ReturnCarryBuffer(numChunks, carryBuffer);
        }
    }

    public void BitwiseAnd(GpuContext context, GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        const uint BlockSize = 256;
        var gridSize = (a.NumWords + BlockSize - 1) / BlockSize;
        if (gridSize == 0)
        {
            gridSize = 1;
        }

        GpuBigIntKernelsWrapper.Launch_BitwiseAndKernel_Device(
            a.DevicePointer, b.DevicePointer, result.DevicePointer, a.NumWords,
            gridDimX: (uint)gridSize, blockDimX: BlockSize);
    }

    public Action<string, IntPtr, int> OnStepCompleted { get; set; }

    public void Mul(GpuContext context, GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        var outWords = a.NumWords + b.NumWords;
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

        GpuBigIntMulKernelsWrapper.Launch_Kernel_MultiplyNtt_Device(
            a.DevicePointer, b.DevicePointer, result.DevicePointer,
            buf1A, buf2A, buf1B, buf2B, tmpBuf,
            twiddle.Forward1, twiddle.Forward2, twiddle.Inverse1, twiddle.Inverse2,
            a.NumWords, b.NumWords, outWords, m,
            gridDimX: gridDim, blockDimX: blockDim,
            sharedMemBytes: (uint)(m * 4)
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

    public void ShiftLeft(GpuContext context, GpuBigInteger a, int shiftBits, GpuBigInteger result)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        uint blockSize = 256;
        var gridSize = (a.NumWords + blockSize - 1) / blockSize;
        if (gridSize == 0)
        {
            gridSize = 1;
        }

        GpuBigIntKernelsWrapper.Launch_ShiftLeftKernel_Device(
            a.DevicePointer, wordShift, bitShift, result.DevicePointer, a.NumWords,
            gridDimX: (uint)gridSize, blockDimX: blockSize);
    }

    public void ShiftRight(GpuContext context, GpuBigInteger a, int shiftBits, GpuBigInteger result)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        uint blockSize = 256;
        var gridSize = (a.NumWords + blockSize - 1) / blockSize;
        if (gridSize == 0)
        {
            gridSize = 1;
        }

        GpuBigIntKernelsWrapper.Launch_ShiftRightKernel_Device(
            a.DevicePointer, wordShift, bitShift, result.DevicePointer, a.NumWords,
            gridDimX: (uint)gridSize, blockDimX: blockSize);
    }

    public bool Square(GpuContext context, IntPtr devicePointer, int logicalWords) =>
        throw new NotImplementedException("Square not implemented in GSharpJitProvider yet.");
}

