using System.Collections.Concurrent;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

/// <summary>
/// Resolves cooperative launch block sizes via <see cref="CooperativeLaunchOccupancy"/> and orchestrates the
/// 2-prime NTT cooperative mega-kernel launches for <see cref="KernelAdvancedMathHandler"/>.
/// </summary>
internal static class CooperativeMegaKernelLauncher
{
    private const string InverseSqrtKernelName = "GpuBigIntSqrtKernels.InverseSqrtNewtonRaphsonMegaKernel";
    private const string ReciprocalKernelName = "GpuBigIntDivRemKernels.ReciprocalNewtonRaphsonMegaKernel";

    private static readonly AsyncLocal<OverrideState?> s_candidateBlockSizesOverride = new();

    /// <summary>
    /// Gets or sets an optional candidate block size list used by tests and benchmarks to force or compare specific
    /// cooperative block sizes without touching the default occupancy cache.
    /// </summary>
    internal static int[]? CandidateBlockSizesOverride
    {
        get => s_candidateBlockSizesOverride.Value?.Candidates;
        set => s_candidateBlockSizesOverride.Value = value is null ? null : new OverrideState(value);
    }

    internal static void InverseSqrtMega(
        KernelAdvancedMathHandler handler,
        GpuWorkspace workspace,
        GpuBigUInt targetOperand,
        GpuBigUInt resultBuffer,
        int marginWords,
        int actualBits,
        int iterations)
    {
        var n = targetOperand.NumWords;
        var marginBits = marginWords * 32;
        var maxOutWords = 4 * n + 3 * marginWords;
        var requiredLen = maxOutWords;

        PrepareLaunchShape(
            workspace,
            requiredLen,
            static () => GpuBigIntSqrtKernels.ResolveInverseSqrtNewtonRaphsonMegaKernel(),
            InverseSqrtKernelName,
            out var blockDim,
            out var gridDim,
            out var sharedMemBytes);

        var constant3Words = 3 * n + 2 * marginWords;
        using var constant3 = new GpuBigUInt(workspace, constant3Words);
        var hostConstant3 = ArrayPool<uint>.Shared.Rent(constant3Words);
        try
        {
            Array.Clear(hostConstant3, 0, constant3Words);
            var bit2B = 2 * n * 32 + 2 * marginBits;
            hostConstant3[bit2B / 32] |= 1u << (bit2B % 32);
            hostConstant3[(bit2B + 1) / 32] |= 1u << ((bit2B + 1) % 32);
            // No stream synchronization: a pageable host-to-device copy is staged before the call returns, so the
            // pooled host buffer may be returned immediately, and the kernels using the constant are ordered on the
            // same stream.
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

        // The kernel resolves carries in chunks of one launch block, so the block size also sizes the block carries.
        using var buffers = new NttWorkspaceBuffers(workspace, requiredLen, maxOutWords, (int)blockDim, handler);
        using var carryBuffer = workspace.RentTemp(2); // long array for carry

        KernelAdvancedMathHandler.ThrowIfBlockCarriesTooSmall(buffers.BlockCarries.NumWords, maxOutWords,
            (int)blockDim);

        var ctx = BuildSqrtContext(targetOperand, resultBuffer, constant3, n, marginWords, actualBits, marginBits,
            iterations, buffers, xSquared, axSquared, diff, nextXUnshifted, nextXShifted, carryBuffer);
        ctx.CorrectionStatus = workspace.CorrectionStatusProxy;

        GpuBigIntSqrtKernels.LaunchInverseSqrtNewtonRaphsonMegaKernelPtr(
            ctx,
            gridDimX: gridDim, blockDimX: blockDim, sharedMemBytes: sharedMemBytes, hStream: workspace.Stream);
        workspace.CorrectionStatus?.MarkPending();
    }

    internal static void ReciprocalMega(
        KernelAdvancedMathHandler handler,
        GpuWorkspace workspace,
        GpuBigUInt targetOperand,
        GpuBigUInt resultBuffer,
        int iterations,
        int targetBits)
    {
        var n = targetOperand.NumWords;
        var resultWords = (targetBits + 31) / 32;
        var maxOutWords = n + 2 * resultWords;
        var requiredLen = maxOutWords;

        PrepareLaunchShape(
            workspace,
            requiredLen,
            static () => GpuBigIntDivRemKernels.ResolveReciprocalNewtonRaphsonMegaKernel(),
            ReciprocalKernelName,
            out var blockDim,
            out var gridDim,
            out var sharedMemBytes);

        var constantWords = n + 2 * resultWords; // Prevent OOB access during ExecuteSub
        using var constant = new GpuBigUInt(workspace, constantWords);
        var hostConstant = ArrayPool<uint>.Shared.Rent(constantWords);
        try
        {
            Array.Clear(hostConstant, 0, constantWords);
            var bitTarget = targetBits + 1;
            hostConstant[bitTarget / 32] |= 1u << (bitTarget % 32);
            // No stream synchronization: a pageable host-to-device copy is staged before the call returns, so the
            // pooled host buffer may be returned immediately, and the kernels using the constant are ordered on the
            // same stream.
            workspace.Provider.CopyHostToDevice(constant.DevicePointer, hostConstant, constantWords, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(hostConstant);
        }

        using var currentProduct = new GpuBigUInt(workspace, n + resultWords);
        using var diff = new GpuBigUInt(workspace, n + resultWords);
        using var nextIter = new GpuBigUInt(workspace, n + 2 * resultWords);

        // The kernel resolves carries in chunks of one launch block, so the block size also sizes the block carries.
        using var buffers = new NttWorkspaceBuffers(workspace, requiredLen, maxOutWords, (int)blockDim, handler);
        using var carryBuffer = workspace.RentTemp(2);

        KernelAdvancedMathHandler.ThrowIfBlockCarriesTooSmall(buffers.BlockCarries.NumWords, maxOutWords,
            (int)blockDim);

        var ctx = BuildReciprocalContext(targetOperand, resultBuffer, constant, n, targetBits, iterations, buffers,
            currentProduct, diff, nextIter, carryBuffer);
        ctx.CorrectionStatus = workspace.CorrectionStatusProxy;

        GpuBigIntDivRemKernels.LaunchReciprocalNewtonRaphsonMegaKernelPtr(
            ctx,
            gridDimX: gridDim, blockDimX: blockDim, sharedMemBytes: sharedMemBytes,
            hStream: workspace.Stream);
        workspace.CorrectionStatus?.MarkPending();
    }

    internal static uint ComputeMegaKernelSharedMemoryBytes(int transformLength)
    {
        var n2 = 1;
        while (n2 * n2 < transformLength)
        {
            n2 *= 2;
        }

        var n1 = transformLength / n2;
        var maxL = transformLength <= 512 ? transformLength : Math.Max(n1, n2);
        var sharedMemBytesNtt = (uint)((maxL + (maxL >> 5)) * 4 + 256);
        const uint SharedMemBytesTranspose = 4224 + 256; // 33 * 32 * 4 + static shared memory
        return Math.Max(sharedMemBytesNtt, SharedMemBytesTranspose);
    }

    internal static uint ComputeClampedGridDim(int transformLength, uint blockDim, int deviceSmCount)
    {
        var gridDim = (uint)((transformLength + blockDim - 1) / blockDim);
        var maxResidentBlocks = (uint)deviceSmCount;
        return gridDim > maxResidentBlocks ? maxResidentBlocks : gridDim;
    }

    internal static bool TryGetCachedDefaultBlockSize(
        IntPtr hfunc,
        int deviceOrdinal,
        uint sharedMemBytes,
        out int blockSize) =>
        CooperativeLaunchOccupancy.TryGetCachedBlockSize(hfunc, deviceOrdinal, sharedMemBytes, out blockSize);

    internal static bool TryGetCachedOverrideBlockSize(
        IntPtr hfunc,
        long contextId,
        int deviceOrdinal,
        uint sharedMemBytes,
        long generation,
        out int blockSize)
    {
        var state = s_candidateBlockSizesOverride.Value;
        if (state is null)
        {
            blockSize = 0;
            return false;
        }

        return state.BlockSizes.TryGetValue(
            (hfunc, contextId, deviceOrdinal, sharedMemBytes, generation),
            out blockSize);
    }

    internal static bool TryGetCachedOverrideBlockSize(
        GpuWorkspace workspace,
        Func<IntPtr> resolveFunction,
        int requiredLen,
        out int blockSize)
    {
        var state = s_candidateBlockSizesOverride.Value;
        if (state is null)
        {
            blockSize = 0;
            return false;
        }

        using var scope = new GpuKernelLoader.CudaContextScope(workspace.IsolatedContext.Handle);
        var hfunc = resolveFunction();
        var sharedMemBytes = ComputeMegaKernelSharedMemoryBytes(TransformLengthFor(requiredLen));
        return state.BlockSizes.TryGetValue(
            (hfunc, CudaModuleCache.CurrentContextId, workspace.DeviceOrdinal, sharedMemBytes,
                CudaModuleCache.Generation),
            out blockSize);
    }

    internal static uint ResolveCooperativeBlockDim(
        GpuWorkspace workspace,
        Func<IntPtr> resolveFunction,
        string kernelName,
        uint sharedMemBytes)
    {
        using var scope = new GpuKernelLoader.CudaContextScope(workspace.IsolatedContext.Handle);
        var hfunc = resolveFunction();
        var overrideState = s_candidateBlockSizesOverride.Value;
        if (overrideState is null)
        {
            return (uint)CooperativeLaunchOccupancy.GetOrChooseBlockSize(
                hfunc,
                workspace.DeviceOrdinal,
                sharedMemBytes,
                kernelName);
        }

        var key = (hfunc, CudaModuleCache.CurrentContextId, workspace.DeviceOrdinal, sharedMemBytes,
            CudaModuleCache.Generation);
        if (overrideState.BlockSizes.TryGetValue(key, out var cached))
        {
            return (uint)cached;
        }

        var chosen = CooperativeLaunchOccupancy.ChooseBlockSize(
            hfunc,
            workspace.DeviceOrdinal,
            sharedMemBytes,
            kernelName,
            overrideState.Candidates);
        overrideState.BlockSizes.TryAdd(key, chosen);
        return (uint)chosen;
    }

    /// <summary>
    /// The transform length for a product of <paramref name="requiredLen"/> words: the power of two that holds its
    /// <c>2 * requiredLen</c> 16-bit coefficients (used by cooperative 2-prime MegaKernels).
    /// </summary>
    /// <exception cref="ArgumentException">The product needs more than <c>2^25</c> coefficients.</exception>
    internal static int TransformLengthFor(int requiredLen)
    {
        const int MaxNttTransformLength = 1 << 25;
        if (requiredLen > (MaxNttTransformLength >> 1))
        {
            throw new ArgumentException(
                $"NTT transform length exceeds the maximum supported limit of 2^25 ({MaxNttTransformLength} " +
                "elements, up to 2^23 words per operand) due to the 2-adicity limit (25) of the NTT primes. " +
                $"Required convolution words: {requiredLen}.",
                nameof(requiredLen));
        }

        var m = 1;
        while (m < requiredLen * 2)
        {
            m *= 2;
        }

        return m;
    }

    private static void PrepareLaunchShape(
        GpuWorkspace workspace,
        int requiredLen,
        Func<IntPtr> resolveFunction,
        string kernelName,
        out uint blockDim,
        out uint gridDim,
        out uint sharedMemBytes)
    {
        var transformLength = TransformLengthFor(requiredLen);
        sharedMemBytes = ComputeMegaKernelSharedMemoryBytes(transformLength);
        blockDim = ResolveCooperativeBlockDim(workspace, resolveFunction, kernelName, sharedMemBytes);
        gridDim = ComputeClampedGridDim(transformLength, blockDim, workspace.DeviceSmCount);
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
                TargetOperand = targetOperand.ToProxy(),
                ResultBuffer = resultBuffer.ToProxy(),
                Constant3 = constant3.ToProxy()
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
                    XSquared = xSquared.ToProxy(),
                    AxSquared = axSquared.ToProxy(),
                    Diff = diff.ToProxy(),
                    NextXUnshifted = nextXUnshifted.ToProxy(),
                    NextXShifted = nextXShifted.ToProxy()
                },
                NttScratch = new GpuBigIntSqrtKernels.AotContexts.NttConvolutionScratch
                {
                    NttBuf1A = buffers.Buf1A.ToProxy(),
                    NttBuf2A = buffers.Buf2A.ToProxy(),
                    NttBuf1B = buffers.Buf1B.ToProxy(),
                    NttBuf2B = buffers.Buf2B.ToProxy(),
                    TmpBuf = buffers.TmpBuf.ToProxy(),
                    UncarriedLow = buffers.UncarriedLow.ToProxy(),
                    UncarriedHigh = buffers.UncarriedHigh.ToProxy(),
                    BlockCarries = buffers.BlockCarries.ToProxy()
                }
            },
            CarryBuffer = new GSharpArrayProxy(carryBuffer.DevicePointer, carryBuffer.NumWords / 2)
        };
    }

    private static GpuBigIntDivRemKernels.AotContexts.NewtonRaphson BuildReciprocalContext(
        GpuBigUInt targetOperand, GpuBigUInt resultBuffer, GpuBigUInt constant,
        int n, int targetBits, int iterations,
        in NttWorkspaceBuffers buffers, GpuBigUInt currentProduct, GpuBigUInt diff,
        GpuBigUInt nextIter, GpuBigUInt carryBuffer)
    {
        return new GpuBigIntDivRemKernels.AotContexts.NewtonRaphson
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
                    CurrentProduct = currentProduct.ToProxy(),
                    Diff = diff.ToProxy(),
                    NextIter = nextIter.ToProxy()
                },
                NttScratch = new GpuBigIntDivRemKernels.AotContexts.NttConvolutionScratch
                {
                    NttBuf1A = buffers.Buf1A.ToProxy(),
                    NttBuf2A = buffers.Buf2A.ToProxy(),
                    NttBuf1B = buffers.Buf1B.ToProxy(),
                    NttBuf2B = buffers.Buf2B.ToProxy(),
                    TmpBuf = buffers.TmpBuf.ToProxy(),
                    UncarriedLow = buffers.UncarriedLow.ToProxy(),
                    UncarriedHigh = buffers.UncarriedHigh.ToProxy(),
                    BlockCarries = buffers.BlockCarries.ToProxy()
                }
            },
            CarryBuffer = new GSharpArrayProxy(carryBuffer.DevicePointer, carryBuffer.NumWords / 2)
        };
    }

    internal readonly struct NttWorkspaceBuffers : IDisposable
    {
        public readonly int TransformLength;
        public readonly KernelAdvancedMathHandler.TwiddleCache Twiddle;
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

    private sealed class OverrideState(int[] candidates)
    {
        public int[] Candidates { get; } = candidates;

        public ConcurrentDictionary<
            (IntPtr Function, long ContextId, int Device, uint DynamicSharedMemBytes, long Generation), int>
            BlockSizes { get; } = new();
    }
}
