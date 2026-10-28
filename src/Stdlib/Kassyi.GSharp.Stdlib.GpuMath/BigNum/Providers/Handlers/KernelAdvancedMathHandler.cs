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

    // ResolveNttCarriesBlockedKernel is launched with blocks of this many threads by Mul and Square.
    private const int MulCarryChunkSize = 1024;

    internal sealed class TwiddleCache
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

        // The kernel resolves carries in chunks of one launch block, so the block size also sizes the block carries.
        const uint BlockDim = 1024u;
        using var buffers = new NttWorkspaceBuffers(workspace, requiredLen, maxOutWords, (int)BlockDim, this);
        using var carryBuffer = workspace.RentTemp(2); // long array for carry

        var m = buffers.TransformLength;

        // The kernel resolves the carries of its products (at most maxOutWords words) in chunks of one launch block.
        ThrowIfBlockCarriesTooSmall(buffers.BlockCarries.NumWords, maxOutWords, (int)BlockDim);
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
        // The status words of the workspace whose stream runs the kernel, which its reads check.
        ctx.Core.CorrectionStatus = workspace.CorrectionStatusPointer;

        GpuBigIntSqrtKernels.LaunchInverseSqrtNewtonRaphsonMegaKernelPtr(
            ctx,
            gridDimX: gridDim, blockDimX: BlockDim, sharedMemBytes: sharedMemBytes, hStream: workspace.Stream);
        workspace.CorrectionStatus?.MarkPending();
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

        // The kernel resolves carries in chunks of one launch block, so the block size also sizes the block carries.
        const uint BlockDim = 1024u;
        using var buffers = new NttWorkspaceBuffers(workspace, requiredLen, maxOutWords, (int)BlockDim, this);
        using var carryBuffer = workspace.RentTemp(2);

        var m = buffers.TransformLength;

        // The kernel resolves the carries of its products (at most maxOutWords words) in chunks of one launch block.
        ThrowIfBlockCarriesTooSmall(buffers.BlockCarries.NumWords, maxOutWords, (int)BlockDim);
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
        // The status words of the workspace whose stream runs the kernel, which its reads check.
        ctx.Core.CorrectionStatus = workspace.CorrectionStatusPointer;

        GpuBigIntDivRemKernels.LaunchReciprocalNewtonRaphsonMegaKernelPtr(
            ctx,
            gridDimX: gridDim, blockDimX: BlockDim, sharedMemBytes: sharedMemBytes
            , hStream: workspace.Stream);
        workspace.CorrectionStatus?.MarkPending();
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
        if (isSquaring)
        {
            // Same buffers, in the same order, as before the owned scratch existed: rented per call.
            using var scratch = NttSquareScratch.Rent(workspace, this, leftOperand.NumWords);
            Square(workspace, leftOperand, resultBuffer, scratch);
            return;
        }

        // Same buffers, in the same order, as before the owned scratch existed: rented per call.
        using var productScratch =
            NttMultiplyScratch.Rent(workspace, this, leftOperand.NumWords + rightOperand.NumWords);
        Multiply(workspace, leftOperand, rightOperand, resultBuffer, productScratch);
    }

    /// <summary>
    /// Multiplies <paramref name="leftOperand"/> by <paramref name="rightOperand"/> into <paramref name="resultBuffer"/> by
    /// NTT, using only the buffers of <paramref name="scratch"/>: the call allocates nothing.
    /// </summary>
    /// <param name="workspace">The workspace whose stream runs the kernels.</param>
    /// <param name="leftOperand">The first factor.</param>
    /// <param name="rightOperand">The second factor (for a square, <see cref="Square"/> needs half the buffers).</param>
    /// <param name="resultBuffer">Receives the low <c>min(scratch.ProductWords, resultBuffer.NumWords)</c> words of the product.</param>
    /// <param name="scratch">The NTT buffers for products of <see cref="NttProductScratch.ProductWords"/> words.</param>
    /// <exception cref="ArgumentException">The factors do not add up to the product length the scratch was made for.</exception>
    internal void Multiply(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer, NttMultiplyScratch scratch)
    {
        var outWords = leftOperand.NumWords + rightOperand.NumWords;
        if (outWords != scratch.ProductWords)
        {
            throw new ArgumentException(
                $"The scratch holds {scratch.ProductWords}-word products; the factors have {leftOperand.NumWords} and {rightOperand.NumWords} words.",
                nameof(scratch));
        }

        var twiddle = scratch.Twiddle;
        var shape = NttLaunchShape.For(scratch.TransformLength);
        var (m, n1, n2) = (shape.TransformLength, shape.N1, shape.N2);

        // 1. Unpack both operands (fuses initial 2D transpose when m > 512)
        GpuBigIntMulKernels.LaunchMultiplyNttUnpackKernelPtr(
            new GpuBigIntMulKernels.AotContexts.NttUnpack
            {
                OperandA = new GpuBigIntMulKernels.AotContexts.NttUnpackOperand
                {
                    Source = leftOperand.DevicePointer,
                    NttBuf1 = scratch.Buf1.DevicePointer,
                    NttBuf2 = scratch.Buf2.DevicePointer,
                    NumWords = leftOperand.NumWords
                },
                OperandB = new GpuBigIntMulKernels.AotContexts.NttUnpackOperand
                {
                    Source = rightOperand.DevicePointer,
                    NttBuf1 = scratch.Buf1B.DevicePointer,
                    NttBuf2 = scratch.Buf2B.DevicePointer,
                    NumWords = rightOperand.NumWords
                },
                Grid = shape.Grid
            },
            gridDimX: shape.GridDim, blockDimX: shape.BlockDim,
            hStream: workspace.Stream);

        // 2. Forward NTTs
        LaunchNttHost(workspace, scratch.Buf1.DevicePointer, scratch.TmpBuf.DevicePointer, twiddle.Forward1, m, n1,
            n2, GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, false);
        LaunchNttHost(workspace, scratch.Buf2.DevicePointer, scratch.TmpBuf.DevicePointer, twiddle.Forward2, m, n1,
            n2, GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, false);
        LaunchNttHost(workspace, scratch.Buf1B.DevicePointer, scratch.TmpBuf.DevicePointer, twiddle.Forward1, m, n1,
            n2, GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, false);
        LaunchNttHost(workspace, scratch.Buf2B.DevicePointer, scratch.TmpBuf.DevicePointer, twiddle.Forward2, m, n1,
            n2, GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, false);

        // 3. Pointwise Multiply
        GpuBigIntMulKernels.LaunchMultiplyNttPointwiseMultiplyKernelPtr(
            scratch.Buf1.DevicePointer, scratch.Buf2.DevicePointer, scratch.Buf1B.DevicePointer,
            scratch.Buf2B.DevicePointer, m,
            gridDimX: shape.GridDim, blockDimX: shape.BlockDim,
            hStream: workspace.Stream);

        InverseNttAndResolveCarries(workspace, shape, twiddle,
            new NttResultBuffers(scratch.Buf1, scratch.Buf2, scratch.TmpBuf, scratch.UncarriedLow,
                scratch.UncarriedHigh, scratch.BlockCarries, MulCarryChunkSize, scratch.NumBlocks),
            outWords, resultBuffer);
    }

    /// <summary>
    /// Squares <paramref name="operand"/> into <paramref name="resultBuffer"/> by NTT, using only the buffers of
    /// <paramref name="scratch"/>: the call allocates nothing, so it can run in a loop or inside a CUDA Graph capture.
    /// </summary>
    /// <param name="workspace">The workspace whose stream runs the kernels.</param>
    /// <param name="operand">The value to square, <see cref="NttSquareScratch.OperandWords"/> words. It is read in full by
    /// the first kernel, so <paramref name="resultBuffer"/> may follow it in the same stream without a copy.</param>
    /// <param name="resultBuffer">Receives the low <c>min(2 * operand.NumWords, resultBuffer.NumWords)</c> words of the square.</param>
    /// <param name="scratch">The NTT buffers for squares of <paramref name="operand"/>'s length.</param>
    /// <exception cref="ArgumentException"><paramref name="operand"/> does not have the length the scratch was made for.</exception>
    internal void Square(GpuWorkspace workspace, GpuBigUInt operand, GpuBigUInt resultBuffer, NttSquareScratch scratch)
    {
        if (operand.NumWords != scratch.OperandWords)
        {
            throw new ArgumentException(
                $"The scratch squares {scratch.OperandWords}-word operands; the operand has {operand.NumWords} words.",
                nameof(operand));
        }

        var twiddle = scratch.Twiddle;
        var shape = NttLaunchShape.For(scratch.TransformLength);
        var (m, n1, n2) = (shape.TransformLength, shape.N1, shape.N2);

        // 1. Unpack single operand (fuses initial 2D transpose when m > 512)
        GpuBigIntMulKernels.LaunchMultiplyNttUnpackSingleKernelPtr(
            new GpuBigIntMulKernels.AotContexts.NttUnpackSingle
            {
                Operand = new GpuBigIntMulKernels.AotContexts.NttUnpackOperand
                {
                    Source = operand.DevicePointer,
                    NttBuf1 = scratch.Buf1.DevicePointer,
                    NttBuf2 = scratch.Buf2.DevicePointer,
                    NumWords = operand.NumWords
                },
                Grid = shape.Grid
            },
            gridDimX: shape.GridDim, blockDimX: shape.BlockDim,
            hStream: workspace.Stream);

        // 2. Forward NTTs (only 2 passes instead of 4)
        LaunchNttHost(workspace, scratch.Buf1.DevicePointer, scratch.TmpBuf.DevicePointer, twiddle.Forward1, m, n1,
            n2, GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, false);
        LaunchNttHost(workspace, scratch.Buf2.DevicePointer, scratch.TmpBuf.DevicePointer, twiddle.Forward2, m, n1,
            n2, GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, false);

        // 3. Pointwise Square
        GpuBigIntMulKernels.LaunchMultiplyNttPointwiseSquareKernelPtr(
            scratch.Buf1.DevicePointer, scratch.Buf2.DevicePointer, m,
            gridDimX: shape.GridDim, blockDimX: shape.BlockDim,
            hStream: workspace.Stream);

        InverseNttAndResolveCarries(workspace, shape, twiddle,
            new NttResultBuffers(scratch.Buf1, scratch.Buf2, scratch.TmpBuf, scratch.UncarriedLow,
                scratch.UncarriedHigh, scratch.BlockCarries, MulCarryChunkSize, scratch.NumBlocks),
            2 * operand.NumWords, resultBuffer);
    }

    /// <summary>Steps 4 to 6 of an NTT product: inverse transforms, CRT and carry resolution into the result.</summary>
    private void InverseNttAndResolveCarries(GpuWorkspace workspace, NttLaunchShape shape, TwiddleCache twiddle,
        NttResultBuffers buffers, int outWords, GpuBigUInt resultBuffer)
    {
        var (m, n1, n2) = (shape.TransformLength, shape.N1, shape.N2);

        // 4. Inverse NTTs
        LaunchNttHost(workspace, buffers.Buf1.DevicePointer, buffers.TmpBuf.DevicePointer, twiddle.Inverse1, m, n1, n2,
            GpuBigIntNttKernels.Prime1, GpuBigIntNttKernels.BarrettMagic1, true);
        LaunchNttHost(workspace, buffers.Buf2.DevicePointer, buffers.TmpBuf.DevicePointer, twiddle.Inverse2, m, n1, n2,
            GpuBigIntNttKernels.Prime2, GpuBigIntNttKernels.BarrettMagic2, true);

        // 5. CRT Coefficients (fuses final 2D transpose read when m > 512)
        GpuBigIntMulKernels.LaunchMultiplyNttComputeCrtKernelPtr(
            buffers.Buf1.DevicePointer, buffers.Buf2.DevicePointer, buffers.UncarriedLow.DevicePointer,
            buffers.UncarriedHigh.DevicePointer, m, n1, n2,
            gridDimX: shape.GridDim, blockDimX: shape.BlockDim,
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

    /// <summary>The 2D split <c>m = N1 * N2</c> of a transform length and the launch size of the per-element kernels.</summary>
    private readonly record struct NttLaunchShape(int TransformLength, int N1, int N2, uint GridDim, uint BlockDim)
    {
        public GpuBigIntMulKernels.AotContexts.NttGrid Grid => new() { M = TransformLength, N1 = N1, N2 = N2 };

        public static NttLaunchShape For(int m)
        {
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
            return new NttLaunchShape(m, n1, n2, gridDim, blockDim);
        }
    }

    /// <summary>The buffers that steps 4 to 6 of an NTT product read and write.</summary>
    private readonly record struct NttResultBuffers(
        GpuBigUInt Buf1,
        GpuBigUInt Buf2,
        GpuBigUInt TmpBuf,
        GpuBigUInt UncarriedLow,
        GpuBigUInt UncarriedHigh,
        GpuBigUInt BlockCarries,
        int ChunkSize,
        int NumBlocks);

    /// <summary>
    /// The NTT buffers of products of <see cref="ProductWords"/> words for <see cref="Square"/> or <see cref="Multiply"/>:
    /// the transform buffers of the operands, the scratch of the transform and of the carries, and the twiddle factors.
    /// </summary>
    /// <remarks>
    /// <c>Allocate</c> makes buffers the scratch owns (<c>new GpuBigUInt</c>, not the workspace's temporary pool) and
    /// releases on <see cref="Dispose"/>, for a caller that multiplies many times (a squaring chain, a CUDA Graph).
    /// <c>Rent</c> takes them from the workspace pool for one call, as
    /// <see cref="Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)"/> does. The twiddle factors are created here, so
    /// that <see cref="Square"/> and <see cref="Multiply"/> neither allocate nor synchronize.
    /// </remarks>
    internal abstract class NttProductScratch : IDisposable
    {
        private protected NttProductScratch(GpuWorkspace workspace, KernelAdvancedMathHandler handler,
            int productWords, bool secondOperand, bool rent)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productWords);
            ProductWords = productWords;
            TransformLength = TransformLengthFor(productWords);
            Twiddle = handler.GetOrCreateTwiddleFactors(workspace, TransformLength);
            NumBlocks = (productWords + MulCarryChunkSize - 1) / MulCarryChunkSize;

            try
            {
                // The order of Mul's pooled path: operand A, operand B, then the shared scratch.
                Buf1 = Take(workspace, TransformLength, rent);
                Buf2 = Take(workspace, TransformLength, rent);
                if (secondOperand)
                {
                    Buf1B = Take(workspace, TransformLength, rent);
                    Buf2B = Take(workspace, TransformLength, rent);
                }

                TmpBuf = Take(workspace, TransformLength, rent);
                UncarriedLow = Take(workspace, TransformLength, rent);
                UncarriedHigh = Take(workspace, TransformLength, rent);
                BlockCarries = Take(workspace, NumBlocks * 2, rent);
            }
            catch
            {
                // Release what was taken before the failure; the default buffers not yet taken ignore Dispose.
                Dispose();
                throw;
            }
        }

        /// <summary>Gets the word count of the products this scratch computes.</summary>
        public int ProductWords { get; }

        /// <summary>Gets the NTT transform length (16-bit coefficients) of a product.</summary>
        public int TransformLength { get; }

        /// <summary>Gets the number of carry chunks of a product.</summary>
        public int NumBlocks { get; }

        internal TwiddleCache Twiddle { get; }

        internal GpuBigUInt Buf1 { get; }
        internal GpuBigUInt Buf2 { get; }
        internal GpuBigUInt Buf1B { get; }
        internal GpuBigUInt Buf2B { get; }
        internal GpuBigUInt TmpBuf { get; }
        internal GpuBigUInt UncarriedLow { get; }
        internal GpuBigUInt UncarriedHigh { get; }
        internal GpuBigUInt BlockCarries { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            Buf1.Dispose();
            Buf2.Dispose();
            Buf1B.Dispose();
            Buf2B.Dispose();
            TmpBuf.Dispose();
            UncarriedLow.Dispose();
            UncarriedHigh.Dispose();
            BlockCarries.Dispose();
        }

        private static GpuBigUInt Take(GpuWorkspace workspace, int words, bool rent) =>
            rent ? workspace.RentTemp(words) : new GpuBigUInt(workspace, words);
    }

    /// <summary>The NTT buffers for squaring an <see cref="OperandWords"/>-word operand with <see cref="Square"/>.</summary>
    internal sealed class NttSquareScratch : NttProductScratch
    {
        private NttSquareScratch(GpuWorkspace workspace, KernelAdvancedMathHandler handler, int operandWords, bool rent)
            : base(workspace, handler, 2 * operandWords, secondOperand: false, rent) =>
            OperandWords = operandWords;

        /// <summary>Gets the word count of the operands this scratch squares.</summary>
        public int OperandWords { get; }

        /// <summary>Creates a scratch whose buffers it owns until <see cref="NttProductScratch.Dispose"/>.</summary>
        /// <param name="workspace">The workspace that allocates the buffers.</param>
        /// <param name="handler">The handler whose twiddle-factor cache supplies the transform.</param>
        /// <param name="operandWords">The word count of the operands to square.</param>
        /// <returns>The new scratch.</returns>
        /// <exception cref="ArgumentException">The square exceeds the NTT transform length limit.</exception>
        public static NttSquareScratch Allocate(GpuWorkspace workspace, KernelAdvancedMathHandler handler,
            int operandWords) => new(workspace, handler, operandWords, rent: false);

        /// <summary>Creates a scratch from the workspace's temporary buffers, for a single square.</summary>
        /// <param name="workspace">The workspace whose temporary buffers are rented.</param>
        /// <param name="handler">The handler whose twiddle-factor cache supplies the transform.</param>
        /// <param name="operandWords">The word count of the operands to square.</param>
        /// <returns>The new scratch.</returns>
        /// <exception cref="ArgumentException">The square exceeds the NTT transform length limit.</exception>
        public static NttSquareScratch Rent(GpuWorkspace workspace, KernelAdvancedMathHandler handler,
            int operandWords) => new(workspace, handler, operandWords, rent: true);
    }

    /// <summary>The NTT buffers for products of two operands with <see cref="Multiply"/>.</summary>
    internal sealed class NttMultiplyScratch : NttProductScratch
    {
        private NttMultiplyScratch(GpuWorkspace workspace, KernelAdvancedMathHandler handler, int productWords,
            bool rent)
            : base(workspace, handler, productWords, secondOperand: true, rent)
        {
        }

        /// <summary>Creates a scratch whose buffers it owns until <see cref="NttProductScratch.Dispose"/>.</summary>
        /// <param name="workspace">The workspace that allocates the buffers.</param>
        /// <param name="handler">The handler whose twiddle-factor cache supplies the transform.</param>
        /// <param name="productWords">The word count of the products: the sum of the factors' word counts.</param>
        /// <returns>The new scratch.</returns>
        /// <exception cref="ArgumentException">The product exceeds the NTT transform length limit.</exception>
        public static NttMultiplyScratch Allocate(GpuWorkspace workspace, KernelAdvancedMathHandler handler,
            int productWords) => new(workspace, handler, productWords, rent: false);

        /// <summary>Creates a scratch from the workspace's temporary buffers, for a single product.</summary>
        /// <param name="workspace">The workspace whose temporary buffers are rented.</param>
        /// <param name="handler">The handler whose twiddle-factor cache supplies the transform.</param>
        /// <param name="productWords">The word count of the product: the sum of the factors' word counts.</param>
        /// <returns>The new scratch.</returns>
        /// <exception cref="ArgumentException">The product exceeds the NTT transform length limit.</exception>
        public static NttMultiplyScratch Rent(GpuWorkspace workspace, KernelAdvancedMathHandler handler,
            int productWords) => new(workspace, handler, productWords, rent: true);
    }

    /// <summary>
    /// The transform length for a product of <paramref name="requiredLen"/> words: the power of two that holds its
    /// <c>2 * requiredLen</c> 16-bit coefficients.
    /// </summary>
    /// <exception cref="ArgumentException">The product needs more than <c>2^25</c> coefficients.</exception>
    private static int TransformLengthFor(int requiredLen)
    {
        const int MaxNttTransformLength = 1 << 25;
        if (requiredLen > (MaxNttTransformLength >> 1))
        {
            throw new ArgumentException(
                $"NTT transform length exceeds the maximum supported limit of 2^25 ({MaxNttTransformLength} elements, up to 2^23 words per operand) due to the 2-adicity limit (25) of the NTT primes. Required convolution words: {requiredLen}.",
                nameof(requiredLen));
        }

        var m = 1;
        while (m < requiredLen * 2)
        {
            m *= 2;
        }

        return m;
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

        // The kernel resolves carries in chunks of one launch block (blockDim threads), not of a fixed size.
        using var buffers = new NttWorkspaceBuffers(workspace, requiredLen, outWords, (int)blockDim, this);
        using var currentProduct = workspace.RentTemp(outWords);
        using var diff = workspace.RentTemp(outWords);
        using var nextIter = workspace.RentTemp(outWords);
        using var nextXUnshifted = workspace.RentTemp(outWords);
        using var nextXShifted = workspace.RentTemp(outWords);
        using var axSquared = workspace.RentTemp(2 * n);
        using var xSquared = workspace.RentTemp(2 * n);
        using var modulusExpanded = workspace.RentTemp(outWords);

        // The kernel resolves the carries of its products (at most outWords words) in chunks of one launch block.
        ThrowIfBlockCarriesTooSmall(buffers.BlockCarries.NumWords, outWords, (int)blockDim);

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

            // Sync after each chunk to let the TDR watchdog reset; the wait also carries the correction status words.
            var status = workspace.CorrectionStatus?.BeginRead();
            try
            {
                CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(workspace.Stream));
            }
            catch
            {
                status?.AbandonRead();
                throw;
            }

            status?.EndRead();
        }
    }

    /// <summary>
    /// Throws when a block-carry buffer holds fewer words than the carry kernel writes for the largest product.
    /// </summary>
    /// <param name="blockCarryWords">The number of words allocated for the block carries.</param>
    /// <param name="maxProductWords">The word count of the largest product whose carries are resolved.</param>
    /// <param name="chunkSize">The carry chunk in words, which is the block size the carry kernel is launched with.</param>
    /// <remarks>
    /// <c>ExecuteResolveNttCarriesBlocked</c> resolves the carries of a product in chunks of one launch block and writes one
    /// carry per chunk, so a product of <paramref name="maxProductWords"/> words writes
    /// <c>ceil(maxProductWords / chunkSize)</c> carries.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The buffer is smaller than the number of carries.</exception>
    internal static void ThrowIfBlockCarriesTooSmall(int blockCarryWords, int maxProductWords, int chunkSize)
    {
        var carryCount = (maxProductWords + chunkSize - 1) / chunkSize;
        if (blockCarryWords < carryCount)
        {
            throw new InvalidOperationException(
                $"The NTT block-carry buffer holds {blockCarryWords} words, but a {maxProductWords}-word product resolved in {chunkSize}-word chunks writes {carryCount} carries.");
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

        /// <param name="workspace">The workspace whose temporary buffers are rented.</param>
        /// <param name="requiredLen">The word count of the largest product, which decides the transform length.</param>
        /// <param name="maxOutWords">The word count of the largest product whose carries are resolved.</param>
        /// <param name="carryChunkSize">
        /// The carry chunk in words: the block size of the launch that runs <c>ExecuteResolveNttCarriesBlocked</c>, which
        /// writes one block carry per chunk.
        /// </param>
        /// <param name="handler">The handler that owns the twiddle-factor cache.</param>
        /// <param name="allocateOperandB">Whether the second operand gets its own NTT buffers (not for squaring).</param>
        public NttWorkspaceBuffers(GpuWorkspace workspace, int requiredLen, int maxOutWords, int carryChunkSize,
            KernelAdvancedMathHandler handler, bool allocateOperandB = true)
        {
            var m = TransformLengthFor(requiredLen);

            TransformLength = m;
            Twiddle = handler.GetOrCreateTwiddleFactors(workspace, m);

            Buf1A = workspace.RentTemp(m);
            Buf2A = workspace.RentTemp(m);
            // Squaring shares the first operand's buffers through borrowed views, which Dispose does not free
            Buf1B = allocateOperandB ? workspace.RentTemp(m) : Buf1A.AsBorrowed();
            Buf2B = allocateOperandB ? workspace.RentTemp(m) : Buf2A.AsBorrowed();
            TmpBuf = workspace.RentTemp(m);

            UncarriedLow = workspace.RentTemp(m);
            UncarriedHigh = workspace.RentTemp(m);

            ChunkSize = carryChunkSize;
            NumBlocks = (maxOutWords + ChunkSize - 1) / ChunkSize;
            BlockCarries = workspace.RentTemp(NumBlocks * 2);
        }

        public void Dispose()
        {
            Buf1A.Dispose();
            Buf2A.Dispose();
            Buf1B.Dispose();
            Buf2B.Dispose();

            TmpBuf.Dispose();
            UncarriedLow.Dispose();
            UncarriedHigh.Dispose();
            BlockCarries.Dispose();
        }
    }
}


