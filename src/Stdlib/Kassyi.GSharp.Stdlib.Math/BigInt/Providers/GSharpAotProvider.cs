using System.Collections.Concurrent;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Providers;

/// <summary>
/// Execution provider implementing arbitrary-precision BigInteger arithmetic via GSharp JIT transpiled GPU kernels.
/// </summary>
/// <remarks><para>Implements: <b>GEN-49, GEN-50</b></para></remarks>
public sealed class GSharpAotProvider : IGpuExecutionProvider
{
    /// <summary>Gets the singleton instance of the <see cref="GSharpAotProvider"/>.</summary>
    public static readonly GSharpAotProvider Instance = new();

    private class TwiddleCache
    {
        public IntPtr Forward1;

        public IntPtr Forward2;

        public IntPtr Inverse1;

        public IntPtr Inverse2;
    }

    private readonly ConcurrentDictionary<int, Lazy<TwiddleCache>> _twiddleCache = new();

    static GSharpAotProvider()
    {
        // Ensure CUDA driver API is initialized before any memory allocations
        CudaDriverApi.cuInit(0);
    }

    private GSharpAotProvider()
    {
    }

    /// <inheritdoc/>
    public void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream)
    {
        if (numWords <= 0)
        {
            return;
        }

        if (devicePointer != IntPtr.Zero)
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8Async(devicePointer, 0, numWords * 4, stream));
        }
    }

    /// <inheritdoc/>
    public IntPtr AllocateMemory(int numWords)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out var dPtr, numWords * 4));
        return dPtr;
    }


    /// <inheritdoc/>
    public void Add(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        var blockDim = System.Math.Min(1024, leftOperand.NumWords);

        GpuBigIntScalarKernelsWrapper.LaunchScalarAddKernelPtr(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: 1, blockDimX: (uint)blockDim, hStream: workspace.Stream);
    }

    /// <inheritdoc/>
    public void Sub(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        var blockDim = System.Math.Min(1024, leftOperand.NumWords);

        GpuBigIntScalarKernelsWrapper.LaunchScalarSubKernelPtr(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: 1, blockDimX: (uint)blockDim, hStream: workspace.Stream);
    }

    /// <inheritdoc/>
    public void Mul(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        var blockDim = System.Math.Min(1024, leftOperand.NumWords);

        GpuBigIntScalarKernelsWrapper.LaunchScalarMulKernelPtr(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: 1, blockDimX: (uint)blockDim, hStream: workspace.Stream);
    }

    /// <inheritdoc/>
    public void BitwiseOr(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.LaunchBitwiseOrKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }

    /// <inheritdoc/>
    public void BitwiseXor(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.LaunchBitwiseXorKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }

    /// <inheritdoc/>
    public void BitwiseNot(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.LaunchBitwiseNotKernelPtr(
            leftOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }

    /// <inheritdoc/>
    public void Gcd(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        var numWords = leftOperand.NumWords;
        using var a = new GpuUIntBigInteger(workspace, numWords);
        using var b = new GpuUIntBigInteger(workspace, numWords);
        workspace.Provider.CopyDeviceToDevice(a.DevicePointer, leftOperand.DevicePointer, numWords, workspace.Stream);
        workspace.Provider.CopyDeviceToDevice(b.DevicePointer, rightOperand.DevicePointer, numWords, workspace.Stream);

        if (a.CompareTo(b) < 0)
        {
            using var t = workspace.RentTemp(numWords);
            workspace.Provider.CopyDeviceToDevice(t.DevicePointer, a.DevicePointer, numWords, workspace.Stream);
            workspace.Provider.CopyDeviceToDevice(a.DevicePointer, b.DevicePointer, numWords, workspace.Stream);
            workspace.Provider.CopyDeviceToDevice(b.DevicePointer, t.DevicePointer, numWords, workspace.Stream);
        }

        using var r = new GpuUIntBigInteger(workspace, numWords);
        using var newA = new GpuUIntBigInteger(workspace, numWords);
        using var newB = new GpuUIntBigInteger(workspace, numWords);
        using var statusBuf = new GpuUIntBigInteger(workspace, 1);
        var statusArray = new uint[1];

        while (!b.IsZero())
        {
            statusArray[0] = 0; // 0 = Running
            workspace.Provider.CopyHostToDevice(statusBuf.DevicePointer, statusArray, 1, workspace.Stream);

            // Execute Lehmer GCD loop entirely on GPU up to 1000 iterations per launch
            GpuBigIntGcdKernelsWrapper.LaunchExecuteLehmerGcdMacroKernelPtr(
                a.DevicePointer, b.DevicePointer, newA.DevicePointer, newB.DevicePointer, statusBuf.DevicePointer,
                numWords, 1000,
                gridDimX: 1, blockDimX: 1, hStream: workspace.Stream);

            workspace.Provider.CopyDeviceToHost(statusArray, statusBuf.DevicePointer, 1, workspace.Stream);

            if (statusArray[0] == 2)
            {
                break; // Finished (b == 0)
            }

            // Fallback: Need Mod due to overflow, size mismatch, or negative intermediate
            GpuBigIntDivRem.Mod(a, b, r);
            workspace.Provider.CopyDeviceToDevice(a.DevicePointer, b.DevicePointer, numWords, workspace.Stream);
            workspace.Provider.CopyDeviceToDevice(b.DevicePointer, r.DevicePointer, numWords, workspace.Stream);
        }

        workspace.Provider.CopyDeviceToDevice(resultBuffer.DevicePointer, a.DevicePointer, numWords, workspace.Stream);
    }

    /// <inheritdoc/>
    public unsafe bool IsZero(GpuWorkspace workspace, GpuUIntBigInteger leftOperand)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);
        using var resultBuf = workspace.RentTemp(1);
        using var scopeMemset1 = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(resultBuf.DevicePointer, 0, 4));

        GpuBigIntCmpKernelsWrapper.LaunchIsZeroKernelPtr(
            leftOperand.DevicePointer, resultBuf.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);

        var res = stackalloc uint[1];
        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)res, resultBuf.DevicePointer, 4));
        return res[0] == 0;
    }

    /// <inheritdoc/>
    public unsafe bool IsOne(GpuWorkspace workspace, GpuUIntBigInteger leftOperand)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);
        using var resultBuf = workspace.RentTemp(1);
        using var scopeMemset2 = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(resultBuf.DevicePointer, 0, 4));

        GpuBigIntCmpKernelsWrapper.LaunchIsOneKernelPtr(
            leftOperand.DevicePointer, resultBuf.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);

        var res = stackalloc uint[1];
        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)res, resultBuf.DevicePointer, 4));
        return res[0] == 0;
    }

    /// <inheritdoc/>
    public unsafe int CompareTo(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand)
    {
        var numWords = System.Math.Max(leftOperand.NumWords, rightOperand.NumWords);
        var dims = CalculateGridDim(numWords);

        GpuUIntBigInteger? leftPad = null;
        GpuUIntBigInteger? rightPad = null;
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
            using var resultBuf = workspace.RentTemp(2);
            using var scopeMemset3 = new GpuKernelLoader.CudaContextScope();
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(resultBuf.DevicePointer, 0, 8));

            GpuBigIntCmpKernelsWrapper.LaunchCompareToKernelPtr(
                leftPtr, rightPtr, resultBuf.DevicePointer, numWords,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);

            var res = stackalloc ulong[1];
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)res, resultBuf.DevicePointer, 8));

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
    public unsafe int GetBitLength(GpuWorkspace workspace, GpuUIntBigInteger leftOperand)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);
        using var resultBuf = workspace.RentTemp(1);
        using var scopeMemset4 = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(resultBuf.DevicePointer, 0, 4));

        GpuBigIntCmpKernelsWrapper.LaunchGetBitLengthKernelPtr(
            leftOperand.DevicePointer, resultBuf.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);

        var res = stackalloc uint[1];
        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)res, resultBuf.DevicePointer, 4));
        return (int)res[0];
    }

    /// <inheritdoc/>
    public void FreeMemory(IntPtr devicePointer)
    {
        if (devicePointer != IntPtr.Zero)
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            CudaDriverApi.cuMemFree_v2(devicePointer);
        }
    }

    /// <inheritdoc/>
    public unsafe void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0)
    {
        if (numWords <= 0)
        {
            return;
        }

        fixed (uint* pHost = hostData)
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(devicePointer, (IntPtr)pHost,
                System.Math.Min(hostData.Length, numWords) * 4, stream));
        }
    }

    /// <inheritdoc/>
    public unsafe void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0)
    {
        if (numWords <= 0)
        {
            return;
        }

        fixed (uint* pHost = hostData)
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            CudaException.ThrowIfError(
                CudaDriverApi.cuMemcpyDtoHAsync_v2((IntPtr)pHost, devicePointer, numWords * 4, stream));
        }
    }

    /// <inheritdoc/>
    public unsafe uint ReadWord(IntPtr devicePointer, int wordOffset = 0)
    {
        var pWord = stackalloc uint[1];
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(
            CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)pWord, IntPtr.Add(devicePointer, wordOffset * 4), 4));
        return *pWord;
    }

    /// <inheritdoc/>
    public unsafe void WriteWord(IntPtr devicePointer, int wordOffset, uint value)
    {
        var pWord = stackalloc uint[1];
        *pWord = value;
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(
            CudaDriverApi.cuMemcpyHtoD_v2(IntPtr.Add(devicePointer, wordOffset * 4), (IntPtr)pWord, 4));
    }

    /// <inheritdoc/>
    public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0)
    {
        if (numWords <= 0 || destPointer == srcPointer)
        {
            return;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoDAsync_v2(destPointer, srcPointer, numWords * 4, stream));
    }

    public void Add(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        // DeviceAddSubParallel requires single block execution for warp-scan carry propagation
        var blockSize = (uint)System.Math.Min(System.Math.Max(leftOperand.NumWords, 32), 1024);
        // Round up to nearest warp
        blockSize = (blockSize + 31) / 32 * 32;

        GpuBigIntKernelsWrapper.LaunchParallelAddKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer,
            leftOperand.NumWords,
            gridDimX: 1, blockDimX: blockSize, hStream: workspace.Stream);
    }

    public void Sub(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        // DeviceAddSubParallel requires single block execution for warp-scan carry propagation
        var blockSize = (uint)System.Math.Min(System.Math.Max(leftOperand.NumWords, 32), 1024);
        // Round up to nearest warp
        blockSize = (blockSize + 31) / 32 * 32;

        GpuBigIntKernelsWrapper.LaunchParallelSubKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer,
            leftOperand.NumWords,
            gridDimX: 1, blockDimX: blockSize, hStream: workspace.Stream);
    }

    /// <inheritdoc/>
    public void BitwiseAnd(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.LaunchBitwiseAndKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }

    /// <inheritdoc/>
    public void InverseSqrtMega(GpuWorkspace workspace, GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer,
        int marginWords, int actualBits, int iterations)
    {
        var n = targetOperand.NumWords;
        var marginBits = marginWords * 32;

        var constant3Words = 3 * n + 2 * marginWords;
        using var constant3 = new GpuUIntBigInteger(workspace, constant3Words);
        constant3.Clear();
        var hostConstant3 = System.Buffers.ArrayPool<uint>.Shared.Rent(constant3Words);
        try
        {
            Array.Clear(hostConstant3, 0, constant3Words);
            var bit2B = 2 * n * 32 + 2 * marginBits;
            hostConstant3[bit2B / 32] |= 1u << (bit2B % 32);
            hostConstant3[(bit2B + 1) / 32] |= 1u << ((bit2B + 1) % 32);
            workspace.Provider.CopyHostToDevice(constant3.DevicePointer, hostConstant3, constant3Words, workspace.Stream);
            CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(workspace.Stream));
        }
        finally
        {
            System.Buffers.ArrayPool<uint>.Shared.Return(hostConstant3);
        }

        using var xSquared = new GpuUIntBigInteger(workspace, 2 * n + 2 * marginWords);
        using var axSquared = new GpuUIntBigInteger(workspace, 3 * n + 2 * marginWords);
        using var diff = new GpuUIntBigInteger(workspace, 3 * n + 2 * marginWords);
        using var nextXUnshifted = new GpuUIntBigInteger(workspace, 4 * n + 3 * marginWords);
        using var nextXShifted = new GpuUIntBigInteger(workspace, 4 * n + 3 * marginWords);

        var maxOutWords = 4 * n + 3 * marginWords;
        var requiredLen = maxOutWords * 2;
        var m = 1;
        while (m < requiredLen)
        {
            m *= 2;
        }

        var twiddle = GetOrCreateTwiddleFactors(workspace, m);

        using var buf1A = workspace.RentTemp(m);
        using var buf2A = workspace.RentTemp(m);
        using var buf1B = workspace.RentTemp(m);
        using var buf2B = workspace.RentTemp(m);
        using var tmpBuf = workspace.RentTemp(m);

        using var uncarriedLow = workspace.RentTemp(m);
        using var uncarriedHigh = workspace.RentTemp(m);

        var chunkSize = 1024;
        var numBlocks = (maxOutWords + chunkSize - 1) / chunkSize;
        using var blockCarries = workspace.RentTemp(numBlocks);

        using var carryBuffer = workspace.RentTemp(2); // long array for carry

        var blockDim = 1024u;
        var gridDim = (uint)((m + blockDim - 1) / blockDim);
        var maxResidentBlocks = (uint)workspace.DeviceSmCount;
        if (gridDim > maxResidentBlocks)
        {
            gridDim = maxResidentBlocks;
        }

        var currentL = m <= 512 ? m : (m / 32);
        var sharedMemBytesNtt = (uint)((currentL + (currentL >> 5)) * 4);
        uint sharedMemBytesTranspose = 4224; // 33 * 32 * 4
        var sharedMemBytes = System.Math.Max(sharedMemBytesNtt, sharedMemBytesTranspose);

        GpuBigIntSqrtKernelsWrapper.LaunchInverseSqrtNewtonRaphsonMegaKernelPtr(
            targetOperand.DevicePointer, resultBuffer.DevicePointer, constant3.DevicePointer,
            xSquared.DevicePointer, axSquared.DevicePointer, diff.DevicePointer,
            nextXUnshifted.DevicePointer, nextXShifted.DevicePointer,
            buf1A.DevicePointer, buf2A.DevicePointer, buf1B.DevicePointer, buf2B.DevicePointer, tmpBuf.DevicePointer,
            uncarriedLow.DevicePointer, uncarriedHigh.DevicePointer, blockCarries.DevicePointer,
            carryBuffer.DevicePointer,
            twiddle.Forward1, twiddle.Forward2, twiddle.Inverse1, twiddle.Inverse2,
            n, marginWords, actualBits, marginBits, iterations, m,
            gridDimX: gridDim, blockDimX: blockDim, sharedMemBytes: sharedMemBytes
            , hStream: workspace.Stream);
    }

    /// <inheritdoc/>
    public void ReciprocalMega(GpuWorkspace workspace, GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer,
        int marginWords, int numBits, int marginBits, int iterations, int targetBits)
    {
        var n = targetOperand.NumWords;
        var resultWords = (targetBits + 31) / 32;

        var constantWords = n + 2 * resultWords; // Prevent OOB access during ExecuteSub
        using var constant = new GpuUIntBigInteger(workspace, constantWords);
        constant.Clear();
        var hostConstant = System.Buffers.ArrayPool<uint>.Shared.Rent(constantWords);
        try
        {
            Array.Clear(hostConstant, 0, constantWords);
            var bitTarget = targetBits + 1;
            hostConstant[bitTarget / 32] |= 1u << (bitTarget % 32);
            workspace.Provider.CopyHostToDevice(constant.DevicePointer, hostConstant, constantWords, workspace.Stream);
            CudaException.ThrowIfError(
                CudaDriverApi.cuStreamSynchronize(workspace.Stream));
        }
        finally
        {
            System.Buffers.ArrayPool<uint>.Shared.Return(hostConstant);
        }

        using var currentProduct = new GpuUIntBigInteger(workspace, n + resultWords);
        using var diff = new GpuUIntBigInteger(workspace, n + resultWords);
        using var nextIter = new GpuUIntBigInteger(workspace, n + 2 * resultWords);

        var maxOutWords = n + 2 * resultWords;
        var requiredLen = maxOutWords * 2;
        var m = 1;
        while (m < requiredLen)
        {
            m *= 2;
        }

        var twiddle = GetOrCreateTwiddleFactors(workspace, m);

        using var buf1A = workspace.RentTemp(m);
        using var buf2A = workspace.RentTemp(m);
        using var buf1B = workspace.RentTemp(m);
        using var buf2B = workspace.RentTemp(m);

        using var uncarriedLow = workspace.RentTemp(m);
        using var uncarriedHigh = workspace.RentTemp(m);

        using var tmpBuf = workspace.RentTemp(m);

        var chunkSize = 1024;
        var numBlocks = (maxOutWords + chunkSize - 1) / chunkSize;
        using var blockCarries = workspace.RentTemp(numBlocks);

        using var carryBuffer = workspace.RentTemp(2);

        var blockDim = 1024u;
        var gridDim = (uint)((m + blockDim - 1) / blockDim);
        var maxResidentBlocks = (uint)workspace.DeviceSmCount;
        if (gridDim > maxResidentBlocks)
        {
            gridDim = maxResidentBlocks;
        }

        var currentL = m <= 512 ? m : (m / 32);
        var sharedMemBytesNtt = (uint)((currentL + (currentL >> 5)) * 4);
        uint sharedMemBytesTranspose = 4224; // 33 * 32 * 4
        var sharedMemBytes = System.Math.Max(sharedMemBytesNtt, sharedMemBytesTranspose);

        GpuBigIntDivRemKernelsWrapper.LaunchReciprocalNewtonRaphsonMegaKernelPtr(
            targetOperand.DevicePointer, resultBuffer.DevicePointer, constant.DevicePointer,
            currentProduct.DevicePointer, diff.DevicePointer, nextIter.DevicePointer,
            buf1A.DevicePointer, buf2A.DevicePointer, buf1B.DevicePointer, buf2B.DevicePointer, tmpBuf.DevicePointer,
            uncarriedLow.DevicePointer, uncarriedHigh.DevicePointer, blockCarries.DevicePointer,
            carryBuffer.DevicePointer,
            twiddle.Forward1, twiddle.Forward2, twiddle.Inverse1, twiddle.Inverse2,
            n, marginWords, targetBits, iterations, m,
            gridDimX: gridDim, blockDimX: blockDim, sharedMemBytes: sharedMemBytes
            , hStream: workspace.Stream);
    }

    /// <inheritdoc/>
    public void Mul(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        var outWords = leftOperand.NumWords + rightOperand.NumWords;
        var requiredLen = outWords * 2;
        var m = 1;
        while (m < requiredLen)
        {
            m *= 2;
        }

        var twiddle = GetOrCreateTwiddleFactors(workspace, m);

        using var buf1A = workspace.RentTemp(m);
        using var buf2A = workspace.RentTemp(m);
        using var buf1B = workspace.RentTemp(m);
        using var buf2B = workspace.RentTemp(m);
        using var tmpBuf = workspace.RentTemp(m);

        using var uncarriedLow = workspace.RentTemp(m);
        using var uncarriedHigh = workspace.RentTemp(m);

        var n2 = 1;
        while (n2 * n2 < m)
        {
            n2 *= 2;
        }

        var n1 = m / n2;
        var maxL = m <= 512 ? m : System.Math.Max(n1, n2);
        var threadsNeeded = maxL / 2;

        var blockDim = (uint)System.Math.Min(1024, System.Math.Max(256, threadsNeeded));
        var gridDim = (uint)((m + blockDim - 1) / blockDim);

        // Global NTT (Non-Cooperative) Launch

        // 1. Unpack
        GpuBigIntMulKernelsWrapper.LaunchMultiplyNttUnpackKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer,
            buf1A.DevicePointer, buf2A.DevicePointer, buf1B.DevicePointer, buf2B.DevicePointer,
            m, leftOperand.NumWords, rightOperand.NumWords,
            gridDimX: gridDim, blockDimX: blockDim
            , hStream: workspace.Stream);

        // 2. Forward NTTs
        LaunchNttHost(workspace, buf1A.DevicePointer, tmpBuf.DevicePointer, twiddle.Forward1, m, n1, n2,
            GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, false);
        LaunchNttHost(workspace, buf2A.DevicePointer, tmpBuf.DevicePointer, twiddle.Forward2, m, n1, n2,
            GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, false);
        LaunchNttHost(workspace, buf1B.DevicePointer, tmpBuf.DevicePointer, twiddle.Forward1, m, n1, n2,
            GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, false);
        LaunchNttHost(workspace, buf2B.DevicePointer, tmpBuf.DevicePointer, twiddle.Forward2, m, n1, n2,
            GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, false);

        // 3. Pointwise Multiply
        GpuBigIntMulKernelsWrapper.LaunchMultiplyNttPointwiseMultiplyKernelPtr(
            buf1A.DevicePointer, buf2A.DevicePointer, buf1B.DevicePointer, buf2B.DevicePointer, m,
            gridDimX: gridDim, blockDimX: blockDim
            , hStream: workspace.Stream);

        // 4. Inverse NTTs
        LaunchNttHost(workspace, buf1A.DevicePointer, tmpBuf.DevicePointer, twiddle.Inverse1, m, n1, n2,
            GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, true);
        LaunchNttHost(workspace, buf2A.DevicePointer, tmpBuf.DevicePointer, twiddle.Inverse2, m, n1, n2,
            GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, true);

        // 5. CRT Coefficients
        GpuBigIntMulKernelsWrapper.LaunchMultiplyNttComputeCrtKernelPtr(
            buf1A.DevicePointer, buf2A.DevicePointer, uncarriedLow.DevicePointer, uncarriedHigh.DevicePointer, m,
            gridDimX: gridDim, blockDimX: blockDim
            , hStream: workspace.Stream);

        // 6. Resolve Carries (Cooperative Block)
        const int ChunkSize = 1024;
        var numBlocks = (outWords + ChunkSize - 1) / ChunkSize;
        using var blockCarries = workspace.RentTemp(numBlocks);

        var writeWords = System.Math.Min(outWords, resultBuffer.NumWords);
        GpuBigIntNttKernelsWrapper.LaunchResolveNttCarriesBlockedKernelPtr(
            uncarriedLow.DevicePointer, uncarriedHigh.DevicePointer, resultBuffer.DevicePointer,
            blockCarries.DevicePointer, m, writeWords, ChunkSize,
            gridDimX: (uint)numBlocks, blockDimX: ChunkSize, sharedMemBytes: ChunkSize * 8
            , hStream: workspace.Stream);

        var applyBlockDim = (uint)System.Math.Min(1024, System.Math.Max(256, numBlocks));
        var applyGridDim = (uint)((numBlocks + applyBlockDim - 1) / applyBlockDim);

        GpuBigIntNttKernelsWrapper.LaunchResolveNttCarriesApplyKernelPtr(
            resultBuffer.DevicePointer, blockCarries.DevicePointer, writeWords, ChunkSize, numBlocks,
            gridDimX: applyGridDim, blockDimX: applyBlockDim
            , hStream: workspace.Stream);
    }

    private void LaunchNttHost(GpuWorkspace workspace, IntPtr buffer, IntPtr tempBuffer, IntPtr twiddle, int m, int n1,
        int n2, uint p,
        ulong pm, bool isInverse)
    {
        if (m <= 512)
        {
            var threads = m / 2;
            GpuBigIntMulKernelsWrapper.LaunchMultiplyNttSharedRowKernelPtr(
                buffer, tempBuffer, twiddle, m, n1, n2, p, pm, false, false, 1, m,
                gridDimX: 1, blockDimX: (uint)threads, sharedMemBytes: (uint)((m + (m >> 5)) * 4)
                , hStream: workspace.Stream);
            return;
        }

        var firstW = isInverse ? n1 : n2;
        var firstH = isInverse ? n2 : n1;
        var secondW = isInverse ? n2 : n1;
        var secondH = isInverse ? n1 : n2;

        var currentBuf = buffer;

        if (!isInverse)
        {
            LaunchTranspose(workspace, currentBuf, tempBuffer, firstW, firstH);
            currentBuf = tempBuffer;
        }

        LaunchSharedRow(workspace, currentBuf, tempBuffer, twiddle, m, n1, n2, p, pm, false, true, firstW, firstH);

        var nextBuf = currentBuf == buffer ? tempBuffer : buffer;
        LaunchTranspose(workspace, currentBuf, nextBuf, firstH, firstW);
        currentBuf = nextBuf;

        LaunchSharedRow(workspace, currentBuf, tempBuffer, twiddle, m, n1, n2, p, pm, false, false, secondW, secondH);

        if (isInverse)
        {
            LaunchTranspose(workspace, currentBuf, buffer, secondH, secondW);
        }
    }

    private void LaunchSharedRow(GpuWorkspace workspace, IntPtr buffer, IntPtr tempBuffer, IntPtr twiddle, int m, int n1,
        int n2, uint p,
        ulong pm, bool applyTwiddleBefore, bool applyTwiddleAfter, int maxBlocks, int currentL)
    {
        var threadsNeeded = currentL / 2;
        var blockDim = (uint)System.Math.Min(1024, System.Math.Max(256, threadsNeeded));
        var gridDim = (uint)System.Math.Min(65535, maxBlocks);

        GpuBigIntMulKernelsWrapper.LaunchMultiplyNttSharedRowKernelPtr(
            buffer, tempBuffer, twiddle, m, n1, n2, p, pm, applyTwiddleBefore, applyTwiddleAfter, maxBlocks,
            currentL,
            gridDimX: gridDim, blockDimX: blockDim,
            sharedMemBytes: (uint)(System.Math.Max(1055, currentL + (currentL >> 5)) * 4)
            , hStream: workspace.Stream);
    }

    private void LaunchTranspose(GpuWorkspace workspace, IntPtr input, IntPtr output, int width, int height)
    {
        var gridDimX = (uint)((width + 31) / 32);
        var gridDimY = (uint)((height + 31) / 32);
        var totalGrid = gridDimX * gridDimY;
        GpuBigIntMulKernelsWrapper.LaunchMultiplyNttTransposeKernelPtr(
            input, output, width, height,
            gridDimX: totalGrid, gridDimY: 1, blockDimX: 256, blockDimY: 1,
            sharedMemBytes: 32 * 33 * 4, hStream: workspace.Stream);
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

    private TwiddleCache GetOrCreateTwiddleFactors(GpuWorkspace workspace, int m)
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

            var dF1 = workspace.Provider.AllocateMemory(key);
            var dF2 = workspace.Provider.AllocateMemory(key);
            var dI1 = workspace.Provider.AllocateMemory(key);
            var dI2 = workspace.Provider.AllocateMemory(key);

            workspace.Provider.CopyHostToDevice(dF1, f1, key, workspace.Stream);
            workspace.Provider.CopyHostToDevice(dF2, f2, key, workspace.Stream);
            workspace.Provider.CopyHostToDevice(dI1, i1, key, workspace.Stream);
            workspace.Provider.CopyHostToDevice(dI2, i2, key, workspace.Stream);

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
    public void ShiftLeft(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, int shiftBits,
        GpuUIntBigInteger resultBuffer)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.LaunchShiftLeftKernelPtr(
            leftOperand.DevicePointer, wordShift, bitShift, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }

    /// <inheritdoc/>
    public void ShiftRight(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, int shiftBits,
        GpuUIntBigInteger resultBuffer)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        var dims = CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.LaunchShiftRightKernelPtr(
            leftOperand.DevicePointer, wordShift, bitShift, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }

    /// <inheritdoc/>
    public bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords)
    {
        using var tempA = new GpuUIntBigInteger(workspace, logicalWords);
        workspace.Provider.CopyDeviceToDevice(tempA.DevicePointer, devicePointer, logicalWords, workspace.Stream);
        using var tempResult = new GpuUIntBigInteger(workspace, logicalWords * 2);
        GpuBigIntMath.Mul(tempA, tempA, tempResult);
        workspace.Provider.CopyDeviceToDevice(devicePointer, tempResult.DevicePointer, logicalWords * 2, workspace.Stream);
        return true;
    }

    /// <inheritdoc/>
    public void BeginCapture(IntPtr stream)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        // CU_STREAM_CAPTURE_MODE_THREAD_LOCAL = 1
        CudaException.ThrowIfError(CudaDriverApi.cuStreamBeginCapture(stream, 1));
    }

    /// <inheritdoc/>
    public IntPtr EndCapture(IntPtr stream)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuStreamEndCapture(stream, out var graph));
        return graph;
    }

    /// <inheritdoc/>
    public IntPtr InstantiateGraph(IntPtr graph)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuGraphInstantiate(out var graphExec, graph, IntPtr.Zero, IntPtr.Zero,
            0));
        return graphExec;
    }

    /// <inheritdoc/>
    public void LaunchGraph(IntPtr graphExec, IntPtr stream)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuGraphLaunch(graphExec, stream));
    }

    /// <inheritdoc/>
    public void DestroyGraph(IntPtr graph, IntPtr graphExec)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        if (graphExec != IntPtr.Zero)
        {
            CudaDriverApi.cuGraphExecDestroy(graphExec);
        }

        if (graph != IntPtr.Zero)
        {
            CudaDriverApi.cuGraphDestroy(graph);
        }
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










