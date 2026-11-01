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
    // An estimate within one step of floor(sqrt(a)) needs one; the rest is a safety margin before the status reports a
    // failure.
    private const int SqrtCorrectionMaxSteps = 4;

    private readonly ConcurrentDictionary<int, Lazy<TwiddleCache>> _twiddleCache = new();

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
    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var isSquaring = leftOperand.DevicePointer == rightOperand.DevicePointer
                         && leftOperand.NumWords == rightOperand.NumWords;
        if (isSquaring)
        {
            using var scratch = Ntt35ConvolutionLauncher.NttSquareScratch.Rent(workspace, leftOperand.NumWords);
            Ntt35ConvolutionLauncher.Square(workspace, leftOperand, resultBuffer, scratch);
            return;
        }

        using var productScratch =
            Ntt35ConvolutionLauncher.NttMultiplyScratch.Rent(workspace, leftOperand.NumWords + rightOperand.NumWords);
        Ntt35ConvolutionLauncher.Multiply(workspace, leftOperand, rightOperand, resultBuffer, productScratch);
    }

    /// <summary>
    /// Multiplies <paramref name="leftOperand"/> by <paramref name="rightOperand"/> into <paramref name="resultBuffer"/> by
    /// 3-prime 35-bit NTT, using only the buffers of <paramref name="scratch"/>: the call allocates nothing.
    /// </summary>
    internal void Multiply(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer, Ntt35ConvolutionLauncher.NttMultiplyScratch scratch) =>
        Ntt35ConvolutionLauncher.Multiply(workspace, leftOperand, rightOperand, resultBuffer, scratch);

    /// <summary>
    /// Squares <paramref name="operand"/> into <paramref name="resultBuffer"/> by tableless 3-prime 35-bit NTT with
    /// fused shared-memory middle passes and coalesced Garner CRT3 carry resolution, allocating nothing.
    /// </summary>
    internal void Square(GpuWorkspace workspace, GpuBigUInt operand, GpuBigUInt resultBuffer,
        Ntt35ConvolutionLauncher.NttProductScratch scratch) =>
        Ntt35ConvolutionLauncher.Square(workspace, operand, resultBuffer, scratch);

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
            ModInverseHostLoopController.Execute(workspace, value, modulus, resultBuffer);
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
        ctx.CorrectionStatus = workspace.CorrectionStatusPointer;

        GpuBigIntSqrtKernels.LaunchInverseSqrtNewtonRaphsonMegaKernelPtr(
            ctx,
            gridDimX: gridDim, blockDimX: BlockDim, sharedMemBytes: sharedMemBytes, hStream: workspace.Stream);
        workspace.CorrectionStatus?.MarkPending();
    }

    /// <inheritdoc />
    public void SqrtFloorCorrection(GpuWorkspace workspace, GpuBigUInt operand, GpuBigUInt rootSquared,
        GpuBigUInt root, GpuBigUInt slack, GpuBigUInt trial)
    {
        // The kernel reads and writes these words without bounds checks, so the sizes are checked on the host.
        var slackWords = operand.NumWords + 1;
        if (rootSquared.NumWords != operand.NumWords || slack.NumWords < slackWords || trial.NumWords < slackWords
            || root.NumWords > operand.NumWords)
        {
            throw new ArgumentException(
                $"SqrtFloorCorrection needs r^2 of {operand.NumWords} words, slack and trial of {slackWords} words and a root of at most {operand.NumWords} words; got {rootSquared.NumWords}, {slack.NumWords}, {trial.NumWords} and {root.NumWords}.");
        }

        // One block: the correction runs on block 0 with block-wide barriers, without a host synchronization.
        GpuBigIntSqrtKernels.LaunchSqrtFloorCorrectionKernelPtr(operand.DevicePointer, rootSquared.DevicePointer,
            root.DevicePointer, slack.DevicePointer, trial.DevicePointer, operand.NumWords, root.NumWords,
            SqrtCorrectionMaxSteps, workspace.CorrectionStatusPointer, gridDimX: 1, blockDimX: 1024,
            hStream: workspace.Stream);
        workspace.CorrectionStatus?.MarkPending();
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
        ctx.CorrectionStatus = workspace.CorrectionStatusPointer;

        GpuBigIntDivRemKernels.LaunchReciprocalNewtonRaphsonMegaKernelPtr(
            ctx,
            gridDimX: gridDim, blockDimX: BlockDim, sharedMemBytes: sharedMemBytes,
            hStream: workspace.Stream);
        workspace.CorrectionStatus?.MarkPending();
    }

    /// <summary>
    /// Executes the ModPow NTT MegaKernel.
    /// </summary>
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

    // --- 2-prime cooperative MegaKernel support ---

    internal sealed class TwiddleCache
    {
        public IntPtr Forward1;
        public IntPtr Forward2;
        public IntPtr Inverse1;
        public IntPtr Inverse2;
    }

    private static GpuBigIntSqrtKernels.AotContexts.NewtonRaphson BuildSqrtContext(
        GpuBigUInt targetOperand, GpuBigUInt resultBuffer, GpuBigUInt constant3,
        int n, int marginWords, int actualBits, int marginBits, int iterations,
        in NttWorkspaceBuffers buffers, GpuBigUInt xSquared, GpuBigUInt axSquared,
        GpuBigUInt diff, GpuBigUInt nextXUnshifted, GpuBigUInt nextXShifted, GpuBigUInt carryBuffer)
    {
        return new GpuBigIntSqrtKernels.AotContexts.NewtonRaphson
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
        };
    }

    private static GpuBigIntDivRemKernels.AotContexts.NewtonRaphson BuildReciprocalContext(
        GpuBigUInt targetOperand, GpuBigUInt resultBuffer, GpuBigUInt constant,
        int n, int marginWords, int targetBits, int iterations,
        in NttWorkspaceBuffers buffers, GpuBigUInt currentProduct, GpuBigUInt diff,
        GpuBigUInt nextIter, GpuBigUInt carryBuffer)
    {
        return new GpuBigIntDivRemKernels.AotContexts.NewtonRaphson
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
        };
    }

    /// <summary>
    /// The transform length for a product of <paramref name="requiredLen"/> words: the power of two that holds its
    /// <c>2 * requiredLen</c> 16-bit coefficients (used by cooperative 2-prime MegaKernels).
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
            var (dF1, dI1) = UploadTwiddles(workspace, NttPrimeArithmetic.Prime1, NttPrimeArithmetic.NttG1, key);
            var (dF2, dI2) = UploadTwiddles(workspace, NttPrimeArithmetic.Prime2, NttPrimeArithmetic.NttG2, key);
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

    private static (IntPtr Forward, IntPtr Inverse) UploadTwiddles(
        GpuWorkspace workspace, uint prime, uint root, int m)
    {
        var w = Ntt35ConvolutionLauncher.PrimeModulusSpec.ModPowScalar(root, (prime - 1u) / (uint)m, prime);
        var iw = Ntt35ConvolutionLauncher.PrimeModulusSpec.ModPowScalar(w, prime - 2u, prime);

        ulong currentWeight = 1;
        ulong currentInverseWeight = 1;
        var forward = new uint[m];
        var inverse = new uint[m];

        for (var i = 0; i < m; i++)
        {
            forward[i] = (uint)currentWeight;
            inverse[i] = (uint)currentInverseWeight;
            currentWeight = (currentWeight * w) % prime;
            currentInverseWeight = (currentInverseWeight * iw) % prime;
        }

        var dForward = workspace.Provider.AllocateMemory(m);
        var dInverse = workspace.Provider.AllocateMemory(m);
        workspace.Provider.CopyHostToDevice(dForward, forward, m, workspace.Stream);
        workspace.Provider.CopyHostToDevice(dInverse, inverse, m, workspace.Stream);
        return (dForward, dInverse);
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

    // --- Shared helpers ---

    /// <summary>
    /// Throws when a block-carry buffer holds fewer words than the carry kernel writes for the largest product.
    /// </summary>
    internal static void ThrowIfBlockCarriesTooSmall(int blockCarryWords, int maxProductWords, int chunkSize)
    {
        var carryCount = (maxProductWords + chunkSize - 1) / chunkSize;
        if (blockCarryWords < carryCount)
        {
            throw new InvalidOperationException(
                $"The NTT block-carry buffer holds {blockCarryWords} words, but a {maxProductWords}-word product resolved in {chunkSize}-word chunks writes {carryCount} carries.");
        }
    }

    internal static void LaunchTranspose(GpuWorkspace workspace, IntPtr input, IntPtr output, int width, int height)
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
