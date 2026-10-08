using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

/// <summary>
/// Dispatches elementary GPU BigInteger arithmetic, bitwise logic, and comparison operations to compiled GSharp device kernels.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Separates basic arithmetic kernel launches (add, sub, scalar, bitwise) from memory management and graph sequencing
/// as part of the <see cref="GSharpAotProvider"/> facade decomposition.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> Directly invokes CUDA kernel launchers without managed allocation.
/// </para>
/// </remarks>
public sealed class AotBasicMathHandler : IGpuBasicMathProvider
{
    private unsafe T EvaluateKernelResult<T>(GpuWorkspace workspace, int resultWords, Action<IntPtr> launchKernel) where T : unmanaged
    {
        var byteSize = sizeof(T);
        using var resultBuf = workspace.RentTemp(resultWords);

        using (new GpuKernelLoader.CudaContextScope())
        {
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(resultBuf.DevicePointer, 0, byteSize));
        }

        launchKernel(resultBuf.DevicePointer);

        T result;
        using (new GpuKernelLoader.CudaContextScope())
        {
            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2((IntPtr)(&result), resultBuf.DevicePointer, byteSize));
        }

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
    public void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernels.LaunchBitwiseOrKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
    {
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernels.LaunchBitwiseXorKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer)
    {
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernels.LaunchBitwiseNotKernelPtr(
            leftOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand)
    {
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);
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
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);
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
        var dims = AotGraphHandler.CalculateGridDim(numWords);

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
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);
        var res = EvaluateKernelResult<uint>(workspace, 1, resultPtr =>
        {
            GpuBigIntCmpKernels.LaunchGetBitLengthKernelPtr(
                leftOperand.DevicePointer, resultPtr, leftOperand.NumWords,
                gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
        });
        return (int)res;
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
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);

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
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);

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
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);

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
