using System.Collections.Concurrent;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

/// <summary>
/// Dispatches advanced arbitrary-precision arithmetic operations (NTT multiplication, Lehmer GCD, iterative reciprocal/rsqrt solvers) to GPU device kernels.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Houses high-complexity GPU kernel orchestrations, including twiddle factor caches for NTT convolution
/// and GPU-side Lehmer GCD reduction loops, isolated from basic arithmetic primitives.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> Scratch memory for intermediate NTT and division passes is acquired through <see cref="GpuWorkspace"/> buffer pooling.
/// </para>
/// </remarks>
public sealed class KernelAdvancedMathHandler : IGpuAdvancedMathProvider
{
    private readonly ConcurrentDictionary<int, Lazy<TwiddleCache>> _twiddleCache = new();

    private class TwiddleCache
    {
        public IntPtr Forward1;
        public IntPtr Forward2;
        public IntPtr Inverse1;
        public IntPtr Inverse2;
    }

    /// <inheritdoc />
    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer)
    {
        var blockDim = Math.Min(1024, leftOperand.NumWords);

        GpuBigIntScalarKernels.LaunchScalarMulKernelPtr(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: 1, blockDimX: (uint)blockDim, hStream: workspace.Stream);
    }


    /// <inheritdoc />
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
        using var statusBuf = workspace.RentTemp(1);

        while (!b.IsZero())
        {
            workspace.Provider.WriteWord(statusBuf.DevicePointer, 0, 0, workspace.Stream); // 0 = Running

            // Execute Lehmer GCD loop entirely on GPU up to 1000 iterations per launch
            unsafe
            {
                var ctx = new GpuBigIntGcdKernels.AotContexts.LehmerGcdContext
                {
                    A = (uint*)a.DevicePointer,
                    B = (uint*)b.DevicePointer,
                    NewA = (uint*)newA.DevicePointer,
                    NewB = (uint*)newB.DevicePointer,
                    Status = (int*)statusBuf.DevicePointer,
                    NumWords = numWords,
                    MaxIterations = 1000
                };
                GpuBigIntGcdKernels.LaunchExecuteLehmerGcdMacroKernel(
                    ctx,
                    gridDimX: 1, blockDimX: 1, hStream: workspace.Stream);
            }

            var status = (int)workspace.Provider.ReadWord(statusBuf.DevicePointer, stream: workspace.Stream);

            if (status == 2)
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

    /// <inheritdoc />
    public unsafe void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer)
    {
        var numWords = value.NumWords;

        var hostVal0 = workspace.Provider.ReadWord(value.DevicePointer, stream: workspace.Stream);
        var hostMod0 = workspace.Provider.ReadWord(modulus.DevicePointer, stream: workspace.Stream);

        if ((hostVal0 & 1) == 0 && (hostMod0 & 1) == 0)
        {
            throw new ArithmeticException("ModInverse failed: The two numbers are not coprime (GCD is at least 2).");
        }

        if ((hostMod0 & 1) == 0)
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
        workspace.Provider.WriteWord(x1.DevicePointer, 0, 1, workspace.Stream);

        // Max iterations for binary GCD is up to 4 * bits
        int maxIterations = numWords * 32 * 4 + 100;

        var ctx = new GpuBigIntGcdKernels.AotContexts.ExtendedBinaryGcdContext
        {
            M = (uint*)modulus.DevicePointer,
            U = (uint*)u.DevicePointer,
            V = (uint*)v.DevicePointer,
            X1 = (uint*)x1.DevicePointer,
            X2 = (uint*)x2.DevicePointer,
            Status = (int*)statusBuf.DevicePointer,
            NumWords = numWords,
            MaxIterations = maxIterations
        };
        GpuBigIntGcdKernels.LaunchExecuteExtendedBinaryGcdMacroKernel(
            ctx,
            gridDimX: 1, blockDimX: 1, hStream: workspace.Stream);

        var statusHost = (int)workspace.Provider.ReadWord(statusBuf.DevicePointer, stream: workspace.Stream);

        switch (statusHost)
        {
            case 2:
                throw new ArithmeticException("ModInverse failed: The two numbers are not coprime (GCD is not 1).");
            case 0:
                throw new Exception("ModInverse failed: Macro kernel did not converge within max iterations.");
            default:
                workspace.Provider.CopyDeviceToDevice(resultBuffer.DevicePointer, x1.DevicePointer, numWords,
                    workspace.Stream);
                break;
        }
    }

    private void ModInverseHostOrchestrated(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus,
        GpuBigUInt resultBuffer) =>
        ModInverseHostLoopController.Execute(workspace, value, modulus, resultBuffer);

    /// <inheritdoc />
    public void InverseSqrtMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int actualBits, int iterations)
    {
        var n = targetOperand.NumWords;
        var marginBits = marginWords * 32;

        var constant3Words = 3 * n + 2 * marginWords;
        using var constant3 = new GpuBigUInt(workspace, constant3Words);
        constant3.Clear();
        var hostConstant3 = ArrayPool<uint>.Shared.Rent(constant3Words);
        try
        {
            Array.Clear(hostConstant3, 0, constant3Words);
            var bit2B = 2 * n * 32 + 2 * marginBits;
            hostConstant3[bit2B / 32] |= 1u << (bit2B % 32);
            hostConstant3[(bit2B + 1) / 32] |= 1u << ((bit2B + 1) % 32);
            // No stream synchronization: a pageable host-to-device copy is staged before the call returns, so the pooled
            // host buffer may be returned immediately, and the kernels using the constant are ordered on the same stream.
            workspace.Provider.CopyHostToDevice(constant3.DevicePointer, hostConstant3, constant3Words,
                workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostConstant3);
        }

        using var xSquared = new GpuBigUInt(workspace, 2 * n + 2 * marginWords);
        using var axSquared = new GpuBigUInt(workspace, 3 * n + 2 * marginWords);
        using var diff = new GpuBigUInt(workspace, 3 * n + 2 * marginWords);
        using var nextXUnshifted = new GpuBigUInt(workspace, 4 * n + 3 * marginWords);
        using var nextXShifted = new GpuBigUInt(workspace, 4 * n + 3 * marginWords);

        var maxOutWords = 4 * n + 3 * marginWords;
        var requiredLen = maxOutWords;

        using var buffers = new NttWorkspaceBuffers(workspace, requiredLen, maxOutWords, this);
        using var carryBuffer = workspace.RentTemp(2); // long array for carry

        var m = buffers.TransformLength;

        const uint BlockDim = 1024u;
        var gridDim = (uint)((m + BlockDim - 1) / BlockDim);
        var maxResidentBlocks = (uint)workspace.DeviceSmCount;
        if (gridDim > maxResidentBlocks)
        {
            gridDim = maxResidentBlocks;
        }

        var n2 = 1;
        while (n2 * n2 < m)
        {
            n2 *= 2;
        }

        var n1 = m / n2;
        var maxL = m <= 512 ? m : Math.Max(n1, n2);
        var sharedMemBytesNtt = (uint)((maxL + (maxL >> 5)) * 4 + 256);
        uint sharedMemBytesTranspose = 4224 + 256; // 33 * 32 * 4 + static shared memory
        var sharedMemBytes = Math.Max(sharedMemBytesNtt, sharedMemBytesTranspose);

        var ctx = BuildSqrtContext(targetOperand, resultBuffer, constant3, n, marginWords, actualBits, marginBits,
            iterations, buffers, xSquared, axSquared, diff, nextXUnshifted, nextXShifted, carryBuffer);

        GpuBigIntSqrtKernels.LaunchInverseSqrtNewtonRaphsonMegaKernelPtr(
            ctx,
            gridDimX: gridDim, blockDimX: BlockDim, sharedMemBytes: sharedMemBytes, hStream: workspace.Stream);
    }

    private static GpuBigIntSqrtKernels.AotContexts.SqrtNewtonRaphson BuildSqrtContext(
        GpuBigUInt targetOperand, GpuBigUInt resultBuffer, GpuBigUInt constant3,
        int n, int marginWords, int actualBits, int marginBits, int iterations,
        in NttWorkspaceBuffers buffers, GpuBigUInt xSquared, GpuBigUInt axSquared,
        GpuBigUInt diff, GpuBigUInt nextXUnshifted, GpuBigUInt nextXShifted, GpuBigUInt carryBuffer)
    {
        return new GpuBigIntSqrtKernels.AotContexts.SqrtNewtonRaphson
        {
            Core = new GpuBigIntSqrtKernels.AotContexts.NewtonRaphson
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
                    TransformLength = buffers.TransformLength,
                    TwiddleFactors = new GpuBigIntSqrtKernels.AotContexts.TwiddleFactor
                    {
                        Forward1 = buffers.Twiddle.Forward1,
                        Forward2 = buffers.Twiddle.Forward2,
                        Inverse1 = buffers.Twiddle.Inverse1,
                        Inverse2 = buffers.Twiddle.Inverse2
                    }
                },
                Workspace = new GpuBigIntSqrtKernels.AotContexts.WorkspaceState
                {
                    Iteration = new GpuBigIntSqrtKernels.AotContexts.IterationScratch
                    {
                        XSquared = xSquared.DevicePointer,
                        AxSquared = axSquared.DevicePointer,
                        Diff = diff.DevicePointer,
                        NextXUnshifted = nextXUnshifted.DevicePointer,
                        NextXShifted = nextXShifted.DevicePointer
                    },
                    NttScratch = new GpuBigIntSqrtKernels.AotContexts.NttConvolutionScratch
                    {
                        NttBuf1A = buffers.Buf1A.DevicePointer,
                        NttBuf2A = buffers.Buf2A.DevicePointer,
                        NttBuf1B = buffers.Buf1B.DevicePointer,
                        NttBuf2B = buffers.Buf2B.DevicePointer,
                        TmpBuf = buffers.TmpBuf.DevicePointer,
                        UncarriedLow = buffers.UncarriedLow.DevicePointer,
                        UncarriedHigh = buffers.UncarriedHigh.DevicePointer,
                        BlockCarries = buffers.BlockCarries.DevicePointer
                    }
                },
                CarryBuffer = carryBuffer.DevicePointer
            }
        };
    }

    /// <inheritdoc />
    public void ReciprocalMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int numBits, int marginBits, int iterations, int targetBits)
    {
        var n = targetOperand.NumWords;
        var resultWords = (targetBits + 31) / 32;

        var constantWords = n + 2 * resultWords; // Prevent OOB access during ExecuteSub
        using var constant = new GpuBigUInt(workspace, constantWords);
        constant.Clear();
        var hostConstant = ArrayPool<uint>.Shared.Rent(constantWords);
        try
        {
            Array.Clear(hostConstant, 0, constantWords);
            var bitTarget = targetBits + 1;
            hostConstant[bitTarget / 32] |= 1u << (bitTarget % 32);
            // No stream synchronization: a pageable host-to-device copy is staged before the call returns, so the pooled
            // host buffer may be returned immediately, and the kernels using the constant are ordered on the same stream.
            workspace.Provider.CopyHostToDevice(constant.DevicePointer, hostConstant, constantWords, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostConstant);
        }

        using var currentProduct = new GpuBigUInt(workspace, n + resultWords);
        currentProduct.Clear();
        using var diff = new GpuBigUInt(workspace, n + resultWords);
        diff.Clear();
        using var nextIter = new GpuBigUInt(workspace, n + 2 * resultWords);
        nextIter.Clear();

        var maxOutWords = n + 2 * resultWords;
        var requiredLen = maxOutWords;

        using var buffers = new NttWorkspaceBuffers(workspace, requiredLen, maxOutWords, this);
        using var carryBuffer = workspace.RentTemp(2);

        var m = buffers.TransformLength;

        const uint BlockDim = 1024u;
        var gridDim = (uint)((m + BlockDim - 1) / BlockDim);
        var maxResidentBlocks = (uint)workspace.DeviceSmCount;
        if (gridDim > maxResidentBlocks)
        {
            gridDim = maxResidentBlocks;
        }

        var n2 = 1;
        while (n2 * n2 < m)
        {
            n2 *= 2;
        }

        var n1 = m / n2;
        var maxL = m <= 512 ? m : Math.Max(n1, n2);
        var sharedMemBytesNtt = (uint)((maxL + (maxL >> 5)) * 4 + 256);
        const uint SharedMemBytesTranspose = 4224 + 256; // 33 * 32 * 4 + static shared memory
        var sharedMemBytes = Math.Max(sharedMemBytesNtt, SharedMemBytesTranspose);

        var ctx = BuildReciprocalContext(targetOperand, resultBuffer, constant, n, marginWords, targetBits,
            iterations, buffers, currentProduct, diff, nextIter, carryBuffer);

        GpuBigIntDivRemKernels.LaunchReciprocalNewtonRaphsonMegaKernelPtr(
            ctx,
            gridDimX: gridDim, blockDimX: BlockDim, sharedMemBytes: sharedMemBytes
            , hStream: workspace.Stream);
    }

    private static GpuBigIntDivRemKernels.AotContexts.ReciprocalNewtonRaphson BuildReciprocalContext(
        GpuBigUInt targetOperand, GpuBigUInt resultBuffer, GpuBigUInt constant,
        int n, int marginWords, int targetBits, int iterations,
        in NttWorkspaceBuffers buffers, GpuBigUInt currentProduct, GpuBigUInt diff,
        GpuBigUInt nextIter, GpuBigUInt carryBuffer)
    {
        return new GpuBigIntDivRemKernels.AotContexts.ReciprocalNewtonRaphson
        {
            Core = new GpuBigIntDivRemKernels.AotContexts.NewtonRaphson
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
                    TransformLength = buffers.TransformLength,
                    TwiddleFactors = new GpuBigIntDivRemKernels.AotContexts.TwiddleFactor
                    {
                        Forward1 = buffers.Twiddle.Forward1,
                        Forward2 = buffers.Twiddle.Forward2,
                        Inverse1 = buffers.Twiddle.Inverse1,
                        Inverse2 = buffers.Twiddle.Inverse2
                    }
                },
                Workspace = new GpuBigIntDivRemKernels.AotContexts.WorkspaceState
                {
                    Iteration = new GpuBigIntDivRemKernels.AotContexts.IterationScratch
                    {
                        CurrentProduct = currentProduct.DevicePointer,
                        Diff = diff.DevicePointer,
                        NextIter = nextIter.DevicePointer
                    },
                    NttScratch = new GpuBigIntDivRemKernels.AotContexts.NttConvolutionScratch
                    {
                        NttBuf1A = buffers.Buf1A.DevicePointer,
                        NttBuf2A = buffers.Buf2A.DevicePointer,
                        NttBuf1B = buffers.Buf1B.DevicePointer,
                        NttBuf2B = buffers.Buf2B.DevicePointer,
                        TmpBuf = buffers.TmpBuf.DevicePointer,
                        UncarriedLow = buffers.UncarriedLow.DevicePointer,
                        UncarriedHigh = buffers.UncarriedHigh.DevicePointer,
                        BlockCarries = buffers.BlockCarries.DevicePointer
                    }
                },
                CarryBuffer = carryBuffer.DevicePointer
            }
        };
    }

    /// <inheritdoc />
    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var isSquaring = leftOperand.DevicePointer == rightOperand.DevicePointer
                         && leftOperand.NumWords == rightOperand.NumWords;
        var outWords = leftOperand.NumWords + rightOperand.NumWords;
        var requiredLen = outWords;

        using var buffers = new NttWorkspaceBuffers(workspace, requiredLen, outWords, this,
            allocateOperandB: !isSquaring);
        var m = buffers.TransformLength;
        var twiddle = buffers.Twiddle;

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

        var unpackGrid = new GpuBigIntMulKernels.AotContexts.NttGrid
        {
            M = m,
            N1 = n1,
            N2 = n2
        };
        var unpackOpA = new GpuBigIntMulKernels.AotContexts.NttUnpackOperand
        {
            Source = leftOperand.DevicePointer,
            NttBuf1 = buffers.Buf1A.DevicePointer,
            NttBuf2 = buffers.Buf2A.DevicePointer,
            NumWords = leftOperand.NumWords
        };

        // Global NTT (Non-Cooperative) Launch
        if (isSquaring)
        {
            // 1. Unpack single operand (fuses initial 2D transpose when m > 512)
            GpuBigIntMulKernels.LaunchMultiplyNttUnpackSingleKernelPtr(
                new GpuBigIntMulKernels.AotContexts.NttUnpackSingle
                {
                    Operand = unpackOpA,
                    Grid = unpackGrid
                },
                gridDimX: gridDim, blockDimX: blockDim,
                hStream: workspace.Stream);

            // 2. Forward NTTs (only 2 passes instead of 4)
            LaunchNttHost(workspace, buffers.Buf1A.DevicePointer, buffers.TmpBuf.DevicePointer, twiddle.Forward1, m, n1,
                n2, GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, false);
            LaunchNttHost(workspace, buffers.Buf2A.DevicePointer, buffers.TmpBuf.DevicePointer, twiddle.Forward2, m, n1,
                n2, GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, false);

            // 3. Pointwise Square
            GpuBigIntMulKernels.LaunchMultiplyNttPointwiseSquareKernelPtr(
                buffers.Buf1A.DevicePointer, buffers.Buf2A.DevicePointer, m,
                gridDimX: gridDim, blockDimX: blockDim,
                hStream: workspace.Stream);
        }
        else
        {
            // 1. Unpack both operands (fuses initial 2D transpose when m > 512)
            GpuBigIntMulKernels.LaunchMultiplyNttUnpackKernelPtr(
                new GpuBigIntMulKernels.AotContexts.NttUnpack
                {
                    OperandA = unpackOpA,
                    OperandB = new GpuBigIntMulKernels.AotContexts.NttUnpackOperand
                    {
                        Source = rightOperand.DevicePointer,
                        NttBuf1 = buffers.Buf1B.DevicePointer,
                        NttBuf2 = buffers.Buf2B.DevicePointer,
                        NumWords = rightOperand.NumWords
                    },
                    Grid = unpackGrid
                },
                gridDimX: gridDim, blockDimX: blockDim,
                hStream: workspace.Stream);

            // 2. Forward NTTs
            LaunchNttHost(workspace, buffers.Buf1A.DevicePointer, buffers.TmpBuf.DevicePointer, twiddle.Forward1, m, n1,
                n2, GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, false);
            LaunchNttHost(workspace, buffers.Buf2A.DevicePointer, buffers.TmpBuf.DevicePointer, twiddle.Forward2, m, n1,
                n2, GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, false);
            LaunchNttHost(workspace, buffers.Buf1B.DevicePointer, buffers.TmpBuf.DevicePointer, twiddle.Forward1, m, n1,
                n2, GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, false);
            LaunchNttHost(workspace, buffers.Buf2B.DevicePointer, buffers.TmpBuf.DevicePointer, twiddle.Forward2, m, n1,
                n2, GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, false);

            // 3. Pointwise Multiply
            GpuBigIntMulKernels.LaunchMultiplyNttPointwiseMultiplyKernelPtr(
                buffers.Buf1A.DevicePointer, buffers.Buf2A.DevicePointer, buffers.Buf1B.DevicePointer,
                buffers.Buf2B.DevicePointer, m,
                gridDimX: gridDim, blockDimX: blockDim,
                hStream: workspace.Stream);
        }

        // 4. Inverse NTTs
        LaunchNttHost(workspace, buffers.Buf1A.DevicePointer, buffers.TmpBuf.DevicePointer, twiddle.Inverse1, m, n1, n2,
            GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, true);
        LaunchNttHost(workspace, buffers.Buf2A.DevicePointer, buffers.TmpBuf.DevicePointer, twiddle.Inverse2, m, n1, n2,
            GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, true);

        // 5. CRT Coefficients (fuses final 2D transpose read when m > 512)
        GpuBigIntMulKernels.LaunchMultiplyNttComputeCrtKernelPtr(
            buffers.Buf1A.DevicePointer, buffers.Buf2A.DevicePointer, buffers.UncarriedLow.DevicePointer,
            buffers.UncarriedHigh.DevicePointer, m, n1, n2,
            gridDimX: gridDim, blockDimX: blockDim,
            hStream: workspace.Stream);

        // 6. Resolve Carries (Cooperative Block)
        var numBlocks = buffers.NumBlocks;
        var writeWords = Math.Min(outWords, resultBuffer.NumWords);
        GpuBigIntNttKernels.LaunchResolveNttCarriesBlockedKernelPtr(
            uncarriedLow: buffers.UncarriedLow.DevicePointer,
            uncarriedHigh: buffers.UncarriedHigh.DevicePointer,
            outputBuffer: resultBuffer.DevicePointer,
            blockCarries: buffers.BlockCarries.DevicePointer,
            transformLength: m,
            maxWordCount: writeWords,
            chunkSize: buffers.ChunkSize,
            gridDimX: (uint)numBlocks, blockDimX: (uint)buffers.ChunkSize,
            sharedMemBytes: (uint)(buffers.ChunkSize * 8), hStream: workspace.Stream);

        var applyBlockDim = (uint)Math.Min(1024, Math.Max(256, numBlocks));
        var applyGridDim = (uint)((numBlocks + applyBlockDim - 1) / applyBlockDim);

        GpuBigIntNttKernels.LaunchResolveNttCarriesApplyKernelPtr(
            resultBuffer.DevicePointer, buffers.BlockCarries.DevicePointer, writeWords, buffers.ChunkSize, numBlocks,
            gridDimX: applyGridDim, blockDimX: applyBlockDim,
            hStream: workspace.Stream);
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

            workspace.Provider.SynchronizeStream(workspace.Stream);

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
                    TemporaryBuffer = buffer,
                    Twiddle = twiddle,
                    Grid = new GpuBigIntMulKernels.AotContexts.NttGrid { M = m, N1 = n1, N2 = n2 },
                    P = p,
                    Pm = pm,
                    ApplyTwiddleBefore = false,
                    ApplyTwiddleAfter = false,
                    MaxBlocks = 1,
                    CurrentL = m
                },
                gridDimX: 1, blockDimX: (uint)threads, sharedMemBytes: (uint)((m + (m >> 5)) * 4),
                hStream: workspace.Stream);
            return;
        }

        var firstW = isInverse ? n1 : n2;
        var firstH = isInverse ? n2 : n1;
        var secondW = isInverse ? n2 : n1;
        var secondH = isInverse ? n1 : n2;

        // Pass 1: In-place row butterflies on buffer (initial transpose already fused in Unpack)
        LaunchSharedRow(workspace, buffer, buffer, twiddle, m, n1, n2, p, pm, false, true, firstW, firstH);

        // Single intermediate transpose: buffer -> tempBuffer
        LaunchTranspose(workspace, buffer, tempBuffer, firstH, firstW);

        // Pass 2: Out-of-place row butterflies reading from tempBuffer and writing directly to buffer
        LaunchSharedRow(workspace, tempBuffer, buffer, twiddle, m, n1, n2, p, pm, false, false, secondW, secondH);
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

    /// <summary>
    /// Executes the ModPow NTT MegaKernel.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="targetOperand">The target operand buffer.</param>
    /// <param name="resultBuffer">The result buffer.</param>
    /// <param name="constant">The constant modulus buffer.</param>
    /// <param name="reciprocalMod">The reciprocal modulus buffer.</param>
    /// <param name="exponent">The exponent buffer.</param>
    /// <param name="n">The number of words.</param>
    /// <param name="targetBits">The target precision in bits.</param>
    /// <param name="gridDim">The grid dimension.</param>
    /// <param name="blockDim">The block dimension.</param>
    public void ExecuteModPowNttMegaKernel(
        GpuWorkspace workspace,
        GpuBigUInt targetOperand, GpuBigUInt resultBuffer, GpuBigUInt constant,
        GpuBigUInt reciprocalMod, GpuBigUInt exponent,
        int n, int targetBits, uint gridDim, uint blockDim)
    {
        var aActualWords = 2 * n;
        var targetWords = (targetBits + 31) / 32;

        var outWords = aActualWords + targetWords;
        var requiredLen = outWords;

        using var buffers = new NttWorkspaceBuffers(workspace, requiredLen, outWords, this);
        using var currentProduct = workspace.RentTemp(outWords);
        using var diff = workspace.RentTemp(outWords);
        using var nextIter = workspace.RentTemp(outWords);
        using var nextXUnshifted = workspace.RentTemp(outWords);
        using var nextXShifted = workspace.RentTemp(outWords);
        using var axSquared = workspace.RentTemp(2 * n);
        using var xSquared = workspace.RentTemp(2 * n);
        using var modulusExpanded = workspace.RentTemp(outWords);

        // For Ntt
        var m = buffers.TransformLength;
        var n2 = 1;
        while (n2 * n2 < m)
        {
            n2 *= 2;
        }

        var ctx = new GpuBigIntDivRemKernels.AotContexts.ModPowNtt
        {
            Core = new GpuBigIntDivRemKernels.AotContexts.NewtonRaphson
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
                    TargetBits = targetBits,
                    Iterations = 1
                },
                Ntt = new GpuBigIntDivRemKernels.AotContexts.Ntt
                {
                    TransformLength = m,
                    TwiddleFactors = new GpuBigIntDivRemKernels.AotContexts.TwiddleFactor
                    {
                        Forward1 = buffers.Twiddle.Forward1,
                        Forward2 = buffers.Twiddle.Forward2,
                        Inverse1 = buffers.Twiddle.Inverse1,
                        Inverse2 = buffers.Twiddle.Inverse2
                    }
                },
                Workspace = new GpuBigIntDivRemKernels.AotContexts.WorkspaceState
                {
                    Iteration = new GpuBigIntDivRemKernels.AotContexts.IterationScratch
                    {
                        CurrentProduct = currentProduct.DevicePointer,
                        Diff = diff.DevicePointer,
                        NextIter = nextIter.DevicePointer,
                        NextXUnshifted = nextXUnshifted.DevicePointer,
                        NextXShifted = nextXShifted.DevicePointer,
                        AxSquared = axSquared.DevicePointer,
                        XSquared = xSquared.DevicePointer,
                        ModulusExpanded = modulusExpanded.DevicePointer
                    },
                    NttScratch = new GpuBigIntDivRemKernels.AotContexts.NttConvolutionScratch
                    {
                        TmpBuf = buffers.TmpBuf.DevicePointer,
                        NttBuf1A = buffers.Buf1A.DevicePointer,
                        NttBuf2A = buffers.Buf2A.DevicePointer,
                        NttBuf1B = buffers.Buf1B.DevicePointer,
                        NttBuf2B = buffers.Buf2B.DevicePointer,
                        UncarriedLow = buffers.UncarriedLow.DevicePointer,
                        UncarriedHigh = buffers.UncarriedHigh.DevicePointer,
                        BlockCarries = buffers.BlockCarries.DevicePointer
                    }
                },
                CarryBuffer = IntPtr.Zero
            },
            ReciprocalMod = reciprocalMod.DevicePointer,
            Exponent = new GpuBigIntDivRemKernels.AotContexts.ExponentSlice
            {
                Words = exponent.DevicePointer,
                TotalWords = exponent.NumWords,
                StartWord = 0,
                EndWord = 0
            }
        };

        int totalWords = exponent.NumWords;
        int chunkWords = 8;
        var n1 = m / n2;
        var maxL = m <= 512 ? m : Math.Max(n1, n2);
        var sharedMemBytesNtt = (uint)((maxL + (maxL >> 5)) * 4 + 256);
        const uint SharedMemBytesTranspose = 4224 + 256;
        var sharedMemBytes = Math.Max(sharedMemBytesNtt, SharedMemBytesTranspose);

        for (int start = 0; start < totalWords; start += chunkWords)
        {
            int end = Math.Min(start + chunkWords, totalWords);
            ctx.Exponent.StartWord = start;
            ctx.Exponent.EndWord = end;

            GpuBigIntDivRemKernels.LaunchModPowNttMegaKernelPtr(
                ctx,
                gridDimX: gridDim, blockDimX: blockDim, sharedMemBytes: sharedMemBytes,
                hStream: workspace.Stream);

            // Sync after each chunk to let the TDR watchdog reset
            CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(workspace.Stream));
        }
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
                Grid = new GpuBigIntMulKernels.AotContexts.NttGrid { M = m, N1 = n1, N2 = n2 },
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

    private readonly struct NttWorkspaceBuffers : IDisposable
    {
        public readonly int TransformLength;
        public readonly TwiddleCache Twiddle;
        public readonly GpuBigUInt Buf1A;
        public readonly GpuBigUInt Buf2A;
        public readonly GpuBigUInt Buf1B;
        public readonly GpuBigUInt Buf2B;
        public readonly GpuBigUInt TmpBuf;
        public readonly GpuBigUInt UncarriedLow;
        public readonly GpuBigUInt UncarriedHigh;
        public readonly GpuBigUInt BlockCarries;
        public readonly int NumBlocks;
        public readonly int ChunkSize;
        private readonly bool _ownsOperandB;

        public NttWorkspaceBuffers(GpuWorkspace workspace, int requiredLen, int maxOutWords,
            KernelAdvancedMathHandler handler, bool allocateOperandB = true)
        {
            var m = 1;
            while (m < requiredLen * 2)
            {
                m *= 2;
            }

            TransformLength = m;
            Twiddle = handler.GetOrCreateTwiddleFactors(workspace, m);
            _ownsOperandB = allocateOperandB;

            Buf1A = workspace.RentTemp(m);
            Buf2A = workspace.RentTemp(m);
            Buf1B = allocateOperandB ? workspace.RentTemp(m) : Buf1A;
            Buf2B = allocateOperandB ? workspace.RentTemp(m) : Buf2A;
            TmpBuf = workspace.RentTemp(m);

            UncarriedLow = workspace.RentTemp(m);
            UncarriedHigh = workspace.RentTemp(m);

            ChunkSize = 1024;
            NumBlocks = (maxOutWords + ChunkSize - 1) / ChunkSize;
            BlockCarries = workspace.RentTemp(NumBlocks * 2);
        }

        public void Dispose()
        {
            Buf1A.Dispose();
            Buf2A.Dispose();
            if (_ownsOperandB)
            {
                Buf1B.Dispose();
                Buf2B.Dispose();
            }

            TmpBuf.Dispose();
            UncarriedLow.Dispose();
            UncarriedHigh.Dispose();
            BlockCarries.Dispose();
        }
    }
}


