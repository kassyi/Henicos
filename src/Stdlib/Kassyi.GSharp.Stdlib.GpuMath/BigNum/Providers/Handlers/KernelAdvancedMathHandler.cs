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

    // The twiddle factors of the 2-prime NTT per CUDA context and transform length. Device memory belongs to the
    // context it was allocated in, and this handler serves every workspace, isolated contexts included, so the key
    // holds the number of the context the upload allocates in (0 in CPU fallback mode, where the pointers are host
    // addresses).
    private readonly ConcurrentDictionary<(long ContextId, int Length), Lazy<TwiddleCache>> _twiddleCache = new();

    /// <inheritdoc />
    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer)
    {
        ScalarResultWidth.ThrowIfNarrower(leftOperand, resultBuffer);
        GpuBigIntScalarKernels.LaunchScalarMulKernelPtr(
            leftOperand.ToProxy(), rightOperand, resultBuffer.ToProxy(), leftOperand.NumWords,
            resultBuffer.NumWords, workspace.CorrectionStatusProxy,
            gridDimX: 1, blockDimX: ScalarResultWidth.BlockSize(leftOperand), hStream: workspace.Stream);
        workspace.CorrectionStatus?.MarkPending();
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

        workspace.Provider.CopyDeviceToDevice(u.DevicePointer, value.DevicePointer, numWords, workspace.Stream);
        workspace.Provider.CopyDeviceToDevice(v.DevicePointer, modulus.DevicePointer, numWords, workspace.Stream);

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
                throw new InvalidOperationException("ModInverse failed: Macro kernel did not converge within max iterations.");
            default:
                workspace.Provider.CopyDeviceToDevice(resultBuffer.DevicePointer, x1.DevicePointer, numWords,
                    workspace.Stream);
                break;
        }
    }

    /// <inheritdoc />
    public void InverseSqrtMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int actualBits, int iterations) =>
        CooperativeMegaKernelLauncher.InverseSqrtMega(
            this, workspace, targetOperand, resultBuffer, marginWords, actualBits, iterations);

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
        GpuBigIntSqrtKernels.LaunchSqrtFloorCorrectionKernelPtr(operand.ToProxy(), rootSquared.ToProxy(),
            root.ToProxy(), slack.ToProxy(), trial.ToProxy(), operand.NumWords, root.NumWords,
            SqrtCorrectionMaxSteps, workspace.CorrectionStatusProxy, gridDimX: 1, blockDimX: 1024,
            hStream: workspace.Stream);
        workspace.CorrectionStatus?.MarkPending();
    }

    /// <inheritdoc />
    public void ReciprocalMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int iterations, int targetBits) =>
        CooperativeMegaKernelLauncher.ReciprocalMega(
            this, workspace, targetOperand, resultBuffer, iterations, targetBits);

    /// <summary>
    /// Executes the ModPow NTT MegaKernel.
    /// </summary>
    /// <param name="workspace">The workspace whose stream runs the kernel and that lends the scratch buffers.</param>
    /// <param name="targetOperand">The base of the power.</param>
    /// <param name="resultBuffer">The buffer that receives the power modulo <paramref name="constant"/>.</param>
    /// <param name="constant">The modulus.</param>
    /// <param name="reciprocalMod">
    /// The reciprocal of the modulus, scaled by <c>2^targetBits</c>, for Barrett reduction.
    /// </param>
    /// <param name="exponent">The exponent, read a few words per launch.</param>
    /// <param name="n">The number of words of the modulus.</param>
    /// <param name="targetBits">The scale of <paramref name="reciprocalMod"/> in bits.</param>
    /// <param name="gridDim">The number of blocks of each launch.</param>
    /// <param name="blockDim">The number of threads per block, which also sizes the block carries.</param>
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
        using var buffers = new CooperativeMegaKernelLauncher.NttWorkspaceBuffers(
            workspace, requiredLen, outWords, (int)blockDim, this);
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
        var sharedMemBytes = CooperativeMegaKernelLauncher.ComputeMegaKernelSharedMemoryBytes(m);

        var ctx = new GpuBigIntDivRemKernels.AotContexts.ModPowNtt
        {
            Core = new GpuBigIntDivRemKernels.AotContexts.NewtonRaphson
            {
                Operands = new GpuBigIntDivRemKernels.AotContexts.Operand
                {
                    TargetOperand = targetOperand.ToProxy(),
                    ResultBuffer = resultBuffer.ToProxy(),
                    Constant = constant.ToProxy()
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
                        CurrentProduct = currentProduct.ToProxy(),
                        Diff = diff.ToProxy(),
                        NextIter = nextIter.ToProxy(),
                        NextXUnshifted = nextXUnshifted.ToProxy(),
                        NextXShifted = nextXShifted.ToProxy(),
                        AxSquared = axSquared.ToProxy(),
                        XSquared = xSquared.ToProxy(),
                        ModulusExpanded = modulusExpanded.ToProxy()
                    },
                    NttScratch = new GpuBigIntDivRemKernels.AotContexts.NttConvolutionScratch
                    {
                        TmpBuf = buffers.TmpBuf.ToProxy(),
                        NttBuf1A = buffers.Buf1A.ToProxy(),
                        NttBuf2A = buffers.Buf2A.ToProxy(),
                        NttBuf1B = buffers.Buf1B.ToProxy(),
                        NttBuf2B = buffers.Buf2B.ToProxy(),
                        UncarriedLow = buffers.UncarriedLow.ToProxy(),
                        UncarriedHigh = buffers.UncarriedHigh.ToProxy(),
                        BlockCarries = buffers.BlockCarries.ToProxy()
                    }
                },
                CarryBuffer = default
            },
            ReciprocalMod = reciprocalMod.ToProxy(),
            Exponent = new GpuBigIntDivRemKernels.AotContexts.ExponentSlice
            {
                Words = exponent.ToProxy(),
                TotalWords = exponent.NumWords,
                StartWord = 0,
                EndWord = 0
            }
        };

        int totalWords = exponent.NumWords;
        int chunkWords = 8;

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
            GpuKernelLoader.ThrowIfDeviceError();
        }
    }

    // --- 2-prime cooperative MegaKernel support ---

    // The twiddle tables of one transform length, each as many words as the length
    internal sealed class TwiddleCache
    {
        public GSharpArrayProxy Forward1 { get; init; }
        public GSharpArrayProxy Forward2 { get; init; }
        public GSharpArrayProxy Inverse1 { get; init; }
        public GSharpArrayProxy Inverse2 { get; init; }
    }

    /// <summary>
    /// Returns the twiddle factors of transform length <paramref name="m"/> in the context current for this thread,
    /// uploading them through <paramref name="workspace"/> the first time; a failed upload is not kept.
    /// </summary>
    internal TwiddleCache GetOrCreateTwiddleFactors(GpuWorkspace workspace, int m)
    {
        var key = (CudaModuleCache.CurrentContextId, m);
        var lazy = _twiddleCache.GetOrAdd(key, k => new Lazy<TwiddleCache>(() =>
        {
            var (dF1, dI1) = UploadTwiddles(workspace, NttPrimeArithmetic.Prime1, NttPrimeArithmetic.NttG1, k.Length);
            var (dF2, dI2) = UploadTwiddles(workspace, NttPrimeArithmetic.Prime2, NttPrimeArithmetic.NttG2, k.Length);
            workspace.Provider.SynchronizeStream(workspace.Stream);

            return new TwiddleCache
            {
                Forward1 = new GSharpArrayProxy(dF1, k.Length),
                Forward2 = new GSharpArrayProxy(dF2, k.Length),
                Inverse1 = new GSharpArrayProxy(dI1, k.Length),
                Inverse2 = new GSharpArrayProxy(dI2, k.Length)
            };
        }));

        try
        {
            return lazy.Value;
        }
        catch
        {
            // Lazy keeps the exception; drop it so that the next call uploads again
            _twiddleCache.TryRemove(new KeyValuePair<(long, int), Lazy<TwiddleCache>>(key, lazy));
            throw;
        }
    }

    /// <summary>
    /// Forgets the twiddle factors of the context numbered <paramref name="contextId"/>, which is being destroyed; its
    /// device memory goes with the context.
    /// </summary>
    internal void ForgetContext(long contextId)
    {
        foreach (var key in _twiddleCache.Keys)
        {
            if (key.ContextId == contextId)
            {
                _twiddleCache.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// The number of transform lengths with twiddle factors kept for the context numbered
    /// <paramref name="contextId"/>.
    /// </summary>
    internal int CountTwiddleFactors(long contextId)
    {
        var count = 0;
        foreach (var key in _twiddleCache.Keys)
        {
            if (key.ContextId == contextId)
            {
                count++;
            }
        }

        return count;
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

    internal static void LaunchTranspose(GpuWorkspace workspace, GSharpArrayProxy input, GSharpArrayProxy output,
        int width, int height)
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
