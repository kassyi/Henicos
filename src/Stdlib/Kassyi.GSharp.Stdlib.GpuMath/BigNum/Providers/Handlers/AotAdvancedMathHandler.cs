using System.Collections.Concurrent;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

public sealed class AotAdvancedMathHandler : IGpuAdvancedMathProvider
{
    private readonly ConcurrentDictionary<int, Lazy<TwiddleCache>> _twiddleCache = new();


    private class TwiddleCache
    {
        public IntPtr Forward1;

        public IntPtr Forward2;

        public IntPtr Inverse1;

        public IntPtr Inverse2;
    }

    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer)
    {
        var blockDim = Math.Min(1024, leftOperand.NumWords);

        GpuBigIntScalarKernels.LaunchScalarMulKernelPtr(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: 1, blockDimX: (uint)blockDim, hStream: workspace.Stream);
    }


    public void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var numWords = leftOperand.NumWords;
        using var a = new GpuBigUInt(workspace, numWords);
        using var b = new GpuBigUInt(workspace, numWords);
        workspace.Provider.CopyDeviceToDevice(a.DevicePointer, leftOperand.DevicePointer, numWords, workspace.Stream);
        workspace.Provider.CopyDeviceToDevice(b.DevicePointer, rightOperand.DevicePointer, numWords, workspace.Stream);

        if (a.CompareTo(b) < 0)
        {
            using var t = workspace.RentTemp(numWords);
            workspace.Provider.CopyDeviceToDevice(t.DevicePointer, a.DevicePointer, numWords, workspace.Stream);
            workspace.Provider.CopyDeviceToDevice(a.DevicePointer, b.DevicePointer, numWords, workspace.Stream);
            workspace.Provider.CopyDeviceToDevice(b.DevicePointer, t.DevicePointer, numWords, workspace.Stream);
        }

        using var r = new GpuBigUInt(workspace, numWords);
        using var newA = new GpuBigUInt(workspace, numWords);
        using var newB = new GpuBigUInt(workspace, numWords);
        using var statusBuf = new GpuBigUInt(workspace, 1);
        var statusArray = new uint[1];

        while (!b.IsZero())
        {
            statusArray[0] = 0; // 0 = Running
            workspace.Provider.CopyHostToDevice(statusBuf.DevicePointer, statusArray, 1, workspace.Stream);

            // Execute Lehmer GCD loop entirely on GPU up to 1000 iterations per launch
            GpuBigIntGcdKernels.LaunchExecuteLehmerGcdMacroKernelPtr(
                a.DevicePointer, b.DevicePointer, newA.DevicePointer, newB.DevicePointer, statusBuf.DevicePointer,
                numWords,
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

    public unsafe void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer)
    {
        var numWords = value.NumWords;

        var hostVal0 = stackalloc uint[1];
        var hostMod0 = stackalloc uint[1];
        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)hostVal0, value.DevicePointer, 4));
        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)hostMod0, modulus.DevicePointer, 4));
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(workspace.Stream));

        if ((hostVal0[0] & 1) == 0 && (hostMod0[0] & 1) == 0)
        {
            throw new ArithmeticException("ModInverse failed: The two numbers are not coprime (GCD is at least 2).");
        }

        if ((hostMod0[0] & 1) == 0)
        {
            // Binary GCD algorithm requires an odd modulus.
            // Fall back to host-orchestrated Euclidean algorithm.
            ModInverseHostOrchestrated(workspace, value, modulus, resultBuffer);
            return;
        }

        using var u = workspace.RentTemp(numWords);
        using var v = workspace.RentTemp(numWords);
        using var x1 = workspace.RentTemp(numWords);
        using var x2 = workspace.RentTemp(numWords);
        using var statusBuf = workspace.RentTemp(1);
        statusBuf.Clear();

        workspace.Provider.CopyDeviceToDevice(u.DevicePointer, value.DevicePointer, numWords, workspace.Stream);
        workspace.Provider.CopyDeviceToDevice(v.DevicePointer, modulus.DevicePointer, numWords, workspace.Stream);

        x1.Clear();
        x2.Clear();
        var hostOne = new uint[] { 1 };
        workspace.Provider.CopyHostToDevice(x1.DevicePointer, hostOne, 1, workspace.Stream);

        // Max iterations for binary GCD is up to 4 * bits
        int maxIterations = numWords * 32 * 4 + 100;

        GpuBigIntGcdKernels.LaunchExecuteExtendedBinaryGcdMacroKernelPtr(
            modulus.DevicePointer, u.DevicePointer, v.DevicePointer, x1.DevicePointer, x2.DevicePointer, statusBuf.DevicePointer,
            numWords, maxIterations,
            gridDimX: 1, blockDimX: 1, hStream: workspace.Stream);

        var statusHost = stackalloc int[1];
        CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(workspace.Stream));
        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)statusHost, statusBuf.DevicePointer, 4));

        if (statusHost[0] == 2)
        {
            throw new ArithmeticException("ModInverse failed: The two numbers are not coprime (GCD is not 1).");
        }
        else if (statusHost[0] == 0)
        {
            throw new Exception("ModInverse failed: Macro kernel did not converge within max iterations.");
        }

        workspace.Provider.CopyDeviceToDevice(resultBuffer.DevicePointer, x1.DevicePointer, numWords, workspace.Stream);
    }

    private void ModInverseHostOrchestrated(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer)
    {
        var numWords = value.NumWords;

        using var i = workspace.RentTemp(numWords);
        using var a = workspace.RentTemp(numWords);
        using var v = workspace.RentTemp(numWords);
        using var d = workspace.RentTemp(numWords);
        using var t = workspace.RentTemp(numWords);
        using var rem = workspace.RentTemp(numWords);
        using var tx = workspace.RentTemp(numWords * 2);
        using var modulus2x = workspace.RentTemp(numWords * 2);
        modulus2x.Clear();
        workspace.Provider.CopyDeviceToDevice(modulus2x.DevicePointer, modulus.DevicePointer, numWords, workspace.Stream);
        using var txMod2x = workspace.RentTemp(numWords * 2);
        using var txMod = workspace.RentTemp(numWords);
        using var nextD = workspace.RentTemp(numWords);

        workspace.Provider.CopyDeviceToDevice(i.DevicePointer, modulus.DevicePointer, numWords, workspace.Stream);
        workspace.Provider.CopyDeviceToDevice(a.DevicePointer, value.DevicePointer, numWords, workspace.Stream);

        v.Clear();
        d.Clear();
        using var one = workspace.RentTemp(numWords);
        one.Clear();
        var hostOne = new uint[] { 1 };
        workspace.Provider.CopyHostToDevice(one.DevicePointer, hostOne, 1, workspace.Stream);
        workspace.Provider.CopyDeviceToDevice(d.DevicePointer, one.DevicePointer, numWords, workspace.Stream);

        while (true)
        {
            if (a.IsZero()) break;
            GpuBigIntDivRem.DivRem(i, a, t, rem);
            workspace.Provider.CopyDeviceToDevice(i.DevicePointer, a.DevicePointer, numWords, workspace.Stream);
            workspace.Provider.CopyDeviceToDevice(a.DevicePointer, rem.DevicePointer, numWords, workspace.Stream);
            tx.Clear();
            GpuBigIntMath.Mul(t, d, tx);
            txMod2x.Clear();
            GpuBigIntDivRem.Mod(tx, modulus2x, txMod2x);
            workspace.Provider.CopyDeviceToDevice(txMod.DevicePointer, txMod2x.DevicePointer, numWords, workspace.Stream);
            int cmp = v.CompareTo(txMod);
            if (cmp >= 0)
            {
                GpuBigIntMath.Sub(v, txMod, nextD);
            }
            else
            {
                GpuBigIntMath.Sub(txMod, v, nextD); 
                GpuBigIntMath.Sub(modulus, nextD, nextD);
            }
            workspace.Provider.CopyDeviceToDevice(v.DevicePointer, d.DevicePointer, numWords, workspace.Stream);
            workspace.Provider.CopyDeviceToDevice(d.DevicePointer, nextD.DevicePointer, numWords, workspace.Stream);
        }

        if (i.CompareTo(one) != 0)
        {
            throw new ArithmeticException("ModInverse failed: The two numbers are not coprime (GCD is not 1).");
        }

        workspace.Provider.CopyDeviceToDevice(resultBuffer.DevicePointer, v.DevicePointer, numWords, workspace.Stream);
    }

    public void InverseSqrtMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int actualBits, int iterations)
    {
        var n = targetOperand.NumWords;
        var marginBits = marginWords * 32;

        var constant3Words = 3 * n + 2 * marginWords;
        using var constant3 = new GpuBigUInt(workspace, constant3Words);
        constant3.Clear();
        var hostConstant3 = System.Buffers.ArrayPool<uint>.Shared.Rent(constant3Words);
        try
        {
            Array.Clear(hostConstant3, 0, constant3Words);
            var bit2B = 2 * n * 32 + 2 * marginBits;
            hostConstant3[bit2B / 32] |= 1u << (bit2B % 32);
            hostConstant3[(bit2B + 1) / 32] |= 1u << ((bit2B + 1) % 32);
            workspace.Provider.CopyHostToDevice(constant3.DevicePointer, hostConstant3, constant3Words,
                workspace.Stream);
            CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(workspace.Stream));
        }
        finally
        {
            System.Buffers.ArrayPool<uint>.Shared.Return(hostConstant3);
        }

        using var xSquared = new GpuBigUInt(workspace, 2 * n + 2 * marginWords);
        using var axSquared = new GpuBigUInt(workspace, 3 * n + 2 * marginWords);
        using var diff = new GpuBigUInt(workspace, 3 * n + 2 * marginWords);
        using var nextXUnshifted = new GpuBigUInt(workspace, 4 * n + 3 * marginWords);
        using var nextXShifted = new GpuBigUInt(workspace, 4 * n + 3 * marginWords);

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
        var sharedMemBytes = Math.Max(sharedMemBytesNtt, sharedMemBytesTranspose);

        var ctx = new GpuBigIntSqrtKernels.AotContexts.SqrtNewtonRaphson
        {
            Operands = new GpuBigIntSqrtKernels.AotContexts.Operand
            {
                TargetOperand = targetOperand.DevicePointer,
                ResultBuffer = resultBuffer.DevicePointer,
                Constant3 = constant3.DevicePointer
            },
            Dimensions = new GpuBigIntSqrtKernels.AotContexts.Dimension
            {
                NumberOfWords = n,
                MarginWords = marginWords,
                ActualBits = actualBits,
                MarginBits = marginBits,
                Iterations = iterations
            },
            Ntt = new GpuBigIntSqrtKernels.AotContexts.Ntt
            {
                TransformLength = m,
                TwiddleFactors = new GpuBigIntSqrtKernels.AotContexts.TwiddleFactor
                {
                    Forward1 = twiddle.Forward1,
                    Forward2 = twiddle.Forward2,
                    Inverse1 = twiddle.Inverse1,
                    Inverse2 = twiddle.Inverse2
                }
            },
            Workspace = new GpuBigIntSqrtKernels.AotContexts.WorkspaceState
            {
                XSquared = xSquared.DevicePointer,
                AxSquared = axSquared.DevicePointer,
                Diff = diff.DevicePointer,
                NextXUnshifted = nextXUnshifted.DevicePointer,
                NextXShifted = nextXShifted.DevicePointer,
                NttBuf1A = buf1A.DevicePointer,
                NttBuf2A = buf2A.DevicePointer,
                NttBuf1B = buf1B.DevicePointer,
                NttBuf2B = buf2B.DevicePointer,
                TmpBuf = tmpBuf.DevicePointer,
                UncarriedLow = uncarriedLow.DevicePointer,
                UncarriedHigh = uncarriedHigh.DevicePointer,
                BlockCarries = blockCarries.DevicePointer
            },
            CarryBuffer = carryBuffer.DevicePointer
        };

        GpuBigIntSqrtKernels.LaunchInverseSqrtNewtonRaphsonMegaKernelPtr(
            ctx,
            gridDimX: gridDim, blockDimX: blockDim, sharedMemBytes: sharedMemBytes, hStream: workspace.Stream);
    }

    public void ReciprocalMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int numBits, int marginBits, int iterations, int targetBits)
    {
        var n = targetOperand.NumWords;
        var resultWords = (targetBits + 31) / 32;

        var constantWords = n + 2 * resultWords; // Prevent OOB access during ExecuteSub
        using var constant = new GpuBigUInt(workspace, constantWords);
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

        using var currentProduct = new GpuBigUInt(workspace, n + resultWords);
        using var diff = new GpuBigUInt(workspace, n + resultWords);
        using var nextIter = new GpuBigUInt(workspace, n + 2 * resultWords);

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
        var sharedMemBytes = Math.Max(sharedMemBytesNtt, sharedMemBytesTranspose);

        var ctx = new GpuBigIntDivRemKernels.AotContexts.ReciprocalNewtonRaphson
        {
            Operands = new GpuBigIntDivRemKernels.AotContexts.Operand
            {
                TargetOperand = targetOperand.DevicePointer,
                ResultBuffer = resultBuffer.DevicePointer,
                Constant = constant.DevicePointer
            },
            Dimensions = new GpuBigIntDivRemKernels.AotContexts.Dimension
            {
                NumberOfWords = n,
                MarginWords = marginWords,
                NumBits = n * 32,
                MarginBits = marginWords * 32,
                TargetBits = targetBits,
                Iterations = iterations
            },
            Ntt = new GpuBigIntDivRemKernels.AotContexts.Ntt
            {
                TransformLength = m,
                TwiddleFactors = new GpuBigIntDivRemKernels.AotContexts.TwiddleFactor
                {
                    Forward1 = twiddle.Forward1,
                    Forward2 = twiddle.Forward2,
                    Inverse1 = twiddle.Inverse1,
                    Inverse2 = twiddle.Inverse2
                }
            },
            Workspace = new GpuBigIntDivRemKernels.AotContexts.WorkspaceState
            {
                CurrentProduct = currentProduct.DevicePointer,
                Diff = diff.DevicePointer,
                NextIter = nextIter.DevicePointer,
                NttBuf1A = buf1A.DevicePointer,
                NttBuf2A = buf2A.DevicePointer,
                NttBuf1B = buf1B.DevicePointer,
                NttBuf2B = buf2B.DevicePointer,
                TmpBuf = tmpBuf.DevicePointer,
                UncarriedLow = uncarriedLow.DevicePointer,
                UncarriedHigh = uncarriedHigh.DevicePointer,
                BlockCarries = blockCarries.DevicePointer
            },
            CarryBuffer = carryBuffer.DevicePointer
        };

        GpuBigIntDivRemKernels.LaunchReciprocalNewtonRaphsonMegaKernelPtr(
            ctx,
            gridDimX: gridDim, blockDimX: blockDim, sharedMemBytes: sharedMemBytes
            , hStream: workspace.Stream);
    }

    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
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
        var maxL = m <= 512 ? m : Math.Max(n1, n2);
        var threadsNeeded = maxL / 2;

        var blockDim = (uint)Math.Min(1024, Math.Max(256, threadsNeeded));
        var gridDim = (uint)((m + blockDim - 1) / blockDim);

        // Global NTT (Non-Cooperative) Launch

        // 1. Unpack
        GpuBigIntMulKernels.LaunchMultiplyNttUnpackKernelPtr(
            new GpuBigIntMulKernels.AotContexts.NttUnpack
            {
                OpA = leftOperand.DevicePointer,
                OpB = rightOperand.DevicePointer,
                NttBuf1A = buf1A.DevicePointer,
                NttBuf2A = buf2A.DevicePointer,
                NttBuf1B = buf1B.DevicePointer,
                NttBuf2B = buf2B.DevicePointer,
                TransformLength = m,
                NumWordsA = leftOperand.NumWords,
                NumWordsB = rightOperand.NumWords
            },
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
        GpuBigIntMulKernels.LaunchMultiplyNttPointwiseMultiplyKernelPtr(
            buf1A.DevicePointer, buf2A.DevicePointer, buf1B.DevicePointer, buf2B.DevicePointer, m,
            gridDimX: gridDim, blockDimX: blockDim
            , hStream: workspace.Stream);

        // 4. Inverse NTTs
        LaunchNttHost(workspace, buf1A.DevicePointer, tmpBuf.DevicePointer, twiddle.Inverse1, m, n1, n2,
            GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, true);
        LaunchNttHost(workspace, buf2A.DevicePointer, tmpBuf.DevicePointer, twiddle.Inverse2, m, n1, n2,
            GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, true);

        // 5. CRT Coefficients
        GpuBigIntMulKernels.LaunchMultiplyNttComputeCrtKernelPtr(
            buf1A.DevicePointer, buf2A.DevicePointer, uncarriedLow.DevicePointer, uncarriedHigh.DevicePointer, m,
            gridDimX: gridDim, blockDimX: blockDim
            , hStream: workspace.Stream);

        // 6. Resolve Carries (Cooperative Block)
        const int ChunkSize = 1024;
        var numBlocks = (outWords + ChunkSize - 1) / ChunkSize;
        using var blockCarries = workspace.RentTemp(numBlocks);

        var writeWords = Math.Min(outWords, resultBuffer.NumWords);
        GpuBigIntNttKernels.LaunchResolveNttCarriesBlockedKernelPtr(
            uncarriedLow: uncarriedLow.DevicePointer,
            uncarriedHigh: uncarriedHigh.DevicePointer,
            outputBuffer: resultBuffer.DevicePointer,
            blockCarries: blockCarries.DevicePointer,
            transformLength: m,
            maxWordCount: writeWords,
            chunkSize: ChunkSize,
            gridDimX: (uint)numBlocks, blockDimX: ChunkSize, sharedMemBytes: ChunkSize * 8, hStream: workspace.Stream);

        var applyBlockDim = (uint)Math.Min(1024, Math.Max(256, numBlocks));
        var applyGridDim = (uint)((numBlocks + applyBlockDim - 1) / applyBlockDim);

        GpuBigIntNttKernels.LaunchResolveNttCarriesApplyKernelPtr(
            resultBuffer.DevicePointer, blockCarries.DevicePointer, writeWords, ChunkSize, numBlocks,
            gridDimX: applyGridDim, blockDimX: applyBlockDim
            , hStream: workspace.Stream);
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

    private void LaunchNttHost(GpuWorkspace workspace, IntPtr buffer, IntPtr tempBuffer, IntPtr twiddle, int m, int n1,
        int n2, uint p,
        ulong pm, bool isInverse)
    {
        if (m <= 512)
        {
            var threads = m / 2;
            GpuBigIntMulKernels.LaunchMultiplyNttSharedRowKernelPtr(
                new GpuBigIntMulKernels.AotContexts.NumberTheoreticTransform
                {
                    Buffer = buffer,
                    TemporaryBuffer = tempBuffer,
                    Twiddle = twiddle,
                    M = m,
                    N1 = n1,
                    N2 = n2,
                    P = p,
                    Pm = pm,
                    ApplyTwiddleBefore = false,
                    ApplyTwiddleAfter = false,
                    MaxBlocks = 1,
                    CurrentL = m
                },
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

    private void LaunchSharedRow(GpuWorkspace workspace, IntPtr buffer, IntPtr tempBuffer, IntPtr twiddle, int m,
        int n1,
        int n2, uint p,
        ulong pm, bool applyTwiddleBefore, bool applyTwiddleAfter, int maxBlocks, int currentL)
    {
        var threadsNeeded = currentL / 2;
        var blockDim = (uint)Math.Min(1024, Math.Max(256, threadsNeeded));
        var gridDim = (uint)Math.Min(65535, maxBlocks);

        GpuBigIntMulKernels.LaunchMultiplyNttSharedRowKernelPtr(
            new GpuBigIntMulKernels.AotContexts.NumberTheoreticTransform
            {
                Buffer = buffer,
                TemporaryBuffer = tempBuffer,
                Twiddle = twiddle,
                M = m,
                N1 = n1,
                N2 = n2,
                P = p,
                Pm = pm,
                ApplyTwiddleBefore = applyTwiddleBefore,
                ApplyTwiddleAfter = applyTwiddleAfter,
                MaxBlocks = maxBlocks,
                CurrentL = currentL
            },
            gridDimX: gridDim, blockDimX: blockDim,
            sharedMemBytes: (uint)(Math.Max(1055, currentL + (currentL >> 5)) * 4)
            , hStream: workspace.Stream);
    }

    private void LaunchTranspose(GpuWorkspace workspace, IntPtr input, IntPtr output, int width, int height)
    {
        var gridDimX = (uint)((width + 31) / 32);
        var gridDimY = (uint)((height + 31) / 32);
        var totalGrid = gridDimX * gridDimY;
        GpuBigIntMulKernels.LaunchMultiplyNttTransposeKernelPtr(
            input, output, width, height,
            gridDimX: totalGrid, gridDimY: 1, blockDimX: 256, blockDimY: 1,
            sharedMemBytes: 32 * 33 * 4, hStream: workspace.Stream);
    }
}
