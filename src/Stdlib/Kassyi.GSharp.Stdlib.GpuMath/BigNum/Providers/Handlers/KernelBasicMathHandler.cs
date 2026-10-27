
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

/// <summary>
/// Dispatches elementary GPU BigInteger arithmetic, bitwise logic, and comparison operations to compiled GSharp device kernels.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Separates basic arithmetic kernel launches (add, sub, scalar, bitwise) from memory management and graph sequencing
/// as part of the <see cref="GSharpKernelProvider"/> facade decomposition.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> Directly invokes CUDA kernel launchers without managed allocation.
/// </para>
/// </remarks>
public sealed class KernelBasicMathHandler : IGpuBasicMathProvider
{
    private unsafe T EvaluateKernelResult<T>(GpuWorkspace workspace, int resultWords, Action<IntPtr> launchKernel)
        where T : unmanaged
    {
        var byteSize = sizeof(T);
        using var resultBuf = workspace.RentTemp(resultWords);

        using (new GpuKernelLoader.CudaContextScope())
        {
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8Async(resultBuf.DevicePointer, 0, byteSize,
                workspace.Stream));
        }

        launchKernel(resultBuf.DevicePointer);

        // This wait also carries the correction status words (GpuCorrectionStatus).
        var status = workspace.CorrectionStatus?.BeginRead();

        T result;
        try
        {
            using (new GpuKernelLoader.CudaContextScope())
            {
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2((IntPtr)(&result), resultBuf.DevicePointer,
                    byteSize, workspace.Stream));
                CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(workspace.Stream));
            }
        }
        catch
        {
            // The wait did not complete, so the next read copies the status words again.
            status?.AbandonRead();
            throw;
        }

        status?.EndRead();

        return result;
    }

    /// <inheritdoc/>
    public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer)
    {
        var blockDim = Math.Min(1024, leftOperand.NumWords);

        GpuBigIntScalarKernels.LaunchScalarAddKernelPtr(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: 1, blockDimX: (uint)blockDim, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer)
    {
        var blockDim = Math.Min(1024, leftOperand.NumWords);

        GpuBigIntScalarKernels.LaunchScalarSubKernelPtr(
            leftOperand.DevicePointer, rightOperand, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: 1, blockDimX: (uint)blockDim, hStream: workspace.Stream);
    }

    /// <inheritdoc/>
    public uint DivideByUInt32(GpuWorkspace workspace, GpuBigUInt dividend, uint divisor,
        GpuBigUInt quotientBuffer)
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }

        return EvaluateKernelResult<uint>(workspace, 1, remPtr =>
        {
            GpuBigIntScalarKernels.LaunchScalarDivByUInt32KernelPtr(
                dividend.DevicePointer, divisor, quotientBuffer.DevicePointer, remPtr, dividend.NumWords,
                gridDimX: 1, blockDimX: 32, hStream: workspace.Stream);
        });
    }


    /// <inheritdoc/>
    public void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var dims = KernelGraphHandler.CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernels.LaunchBitwiseOrKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var dims = KernelGraphHandler.CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernels.LaunchBitwiseXorKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer)
    {
        var dims = KernelGraphHandler.CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernels.LaunchBitwiseNotKernelPtr(
            leftOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand)
    {
        var dims = KernelGraphHandler.CalculateGridDim(leftOperand.NumWords);
        var res = EvaluateKernelResult<uint>(workspace, 1, resultPtr =>
        {
            GpuBigIntCmpKernels.LaunchIsZeroKernelPtr(
                leftOperand.DevicePointer, resultPtr, leftOperand.NumWords,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
        });
        return res == 0;
    }


    /// <inheritdoc/>
    public bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand)
    {
        var dims = KernelGraphHandler.CalculateGridDim(leftOperand.NumWords);
        var res = EvaluateKernelResult<uint>(workspace, 1, resultPtr =>
        {
            GpuBigIntCmpKernels.LaunchIsOneKernelPtr(
                leftOperand.DevicePointer, resultPtr, leftOperand.NumWords,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
        });
        return res == 0;
    }


    /// <inheritdoc/>
    public int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand)
    {
        var numWords = Math.Max(leftOperand.NumWords, rightOperand.NumWords);
        var dims = KernelGraphHandler.CalculateGridDim(numWords);

        GpuBigUInt? leftPad = null;
        GpuBigUInt? rightPad = null;
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

            var val = EvaluateKernelResult<ulong>(workspace, 2, resultPtr =>
            {
                GpuBigIntCmpKernels.LaunchCompareToKernelPtr(
                    leftPtr, rightPtr, resultPtr, numWords,
                    gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
            });

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
    public int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand)
    {
        var dims = KernelGraphHandler.CalculateGridDim(leftOperand.NumWords);
        var res = EvaluateKernelResult<uint>(workspace, 1, resultPtr =>
        {
            GpuBigIntCmpKernels.LaunchGetBitLengthKernelPtr(
                leftOperand.DevicePointer, resultPtr, leftOperand.NumWords,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
        });
        return (int)res;
    }

    /// <inheritdoc/>
    public void ReadLeadingBits(GpuWorkspace workspace, GpuBigUInt value, out int bitLength, out ulong leadingBits)
    {
        var dims = KernelGraphHandler.CalculateGridDim(value.NumWords);

        // Result words: [0] bit length (GetBitLengthKernel), [1] and [2] the leading 64 bits, [3] unused.
        var res = EvaluateKernelResult<UInt128>(workspace, 4, resultPtr =>
        {
            GpuBigIntCmpKernels.LaunchGetBitLengthKernelPtr(
                value.DevicePointer, resultPtr, value.NumWords,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
            GpuBigIntCmpKernels.LaunchLeadingBitsKernelPtr(
                value.DevicePointer, resultPtr, value.NumWords,
                gridDimX: 1, blockDimX: 32, hStream: workspace.Stream);
        });
        bitLength = (int)(uint)res;
        leadingBits = (ulong)(res >> 32);
    }


    /// <inheritdoc />
    public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        // DeviceAddSubParallel requires single block execution for warp-scan carry propagation
        var blockSize = (uint)Math.Min(Math.Max(leftOperand.NumWords, 32), 1024);
        // Round up to nearest warp
        blockSize = (blockSize + 31) / 32 * 32;

        GpuBigIntKernels.LaunchParallelAddKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer,
            leftOperand.NumWords,
            gridDimX: 1, blockDimX: blockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc />
    public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        // DeviceAddSubParallel requires single block execution for warp-scan carry propagation
        var blockSize = (uint)Math.Min(Math.Max(leftOperand.NumWords, 32), 1024);
        // Round up to nearest warp
        blockSize = (blockSize + 31) / 32 * 32;

        GpuBigIntKernels.LaunchParallelSubKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer,
            leftOperand.NumWords,
            gridDimX: 1, blockDimX: blockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var dims = KernelGraphHandler.CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernels.LaunchBitwiseAndKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits,
        GpuBigUInt resultBuffer)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        var dims = KernelGraphHandler.CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernels.LaunchShiftLeftKernelPtr(
            leftOperand.DevicePointer, wordShift, bitShift, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits,
        GpuBigUInt resultBuffer)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        var dims = KernelGraphHandler.CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernels.LaunchShiftRightKernelPtr(
            leftOperand.DevicePointer, wordShift, bitShift, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords)
    {
        using var tempA = new GpuBigUInt(workspace, logicalWords);
        workspace.Provider.CopyDeviceToDevice(tempA.DevicePointer, devicePointer, logicalWords, workspace.Stream);
        using var tempResult = new GpuBigUInt(workspace, logicalWords * 2);
        GpuBigIntMath.Mul(tempA, tempA, tempResult);
        workspace.Provider.CopyDeviceToDevice(devicePointer, tempResult.DevicePointer, logicalWords * 2,
            workspace.Stream);
        return true;
    }
}
