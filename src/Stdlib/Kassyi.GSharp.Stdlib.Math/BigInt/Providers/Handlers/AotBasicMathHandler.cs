using System.Collections.Concurrent;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
namespace Kassyi.GSharp.Stdlib.Math.BigInt.Providers.Handlers;

public sealed class AotBasicMathHandler
{


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
    public void BitwiseOr(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.LaunchBitwiseOrKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public void BitwiseXor(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
    {
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.LaunchBitwiseXorKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public void BitwiseNot(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer)
    {
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.LaunchBitwiseNotKernelPtr(
            leftOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public unsafe bool IsZero(GpuWorkspace workspace, GpuUIntBigInteger leftOperand)
    {
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);
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
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);
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
        var dims = AotGraphHandler.CalculateGridDim(numWords);

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
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);
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
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);

        GpuBigIntKernelsWrapper.LaunchBitwiseAndKernelPtr(
            leftOperand.DevicePointer, rightOperand.DevicePointer, resultBuffer.DevicePointer, leftOperand.NumWords,
            gridDimX: dims.GridSize, blockDimX: dims.BlockSize, hStream: workspace.Stream);
    }


    /// <inheritdoc/>
    public void ShiftLeft(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, int shiftBits,
        GpuUIntBigInteger resultBuffer)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);

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
        var dims = AotGraphHandler.CalculateGridDim(leftOperand.NumWords);

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
        workspace.Provider.CopyDeviceToDevice(devicePointer, tempResult.DevicePointer, logicalWords * 2,
            workspace.Stream);
        return true;
    }

}
