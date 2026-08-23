using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.GpuBigInt.Expressions;

namespace Kassyi.GSharp.GpuBigInt;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
public class GpuBigInteger : IDisposable, IGpuBigIntExpression
{
    public IntPtr DevicePointer { get; private set; }
    public int NumWords { get; }

    public GpuContext Context { get; }

    internal bool IsTemp { get; set; }

    private bool _disposed;

    public GpuBigInteger(GpuContext context, int numWords)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        NumWords = numWords;

        if (Context.Engine == GpuExecutionEngine.GSharpJit)
        {
            CudaException.ThrowIfError(
                CudaDriverApi.cuMemAlloc_v2(out var dPtr, numWords * 4));
            DevicePointer = dPtr;
        }
        else
        {
            DevicePointer = NativeMethods.GpuBigInt_Alloc(numWords);
        }

        if (DevicePointer == IntPtr.Zero)
        {
            throw new OutOfMemoryException(
                $"Failed to allocate GPU memory for {numWords} words ({numWords * 4} bytes).");
        }
    }

    public GpuBigInteger(GpuContext context, uint[] hostData) : this(context, hostData.Length)
    {
        if (Context.Engine == GpuExecutionEngine.GSharpJit)
        {
            var handle = GCHandle.Alloc(hostData, GCHandleType.Pinned);
            try
            {
                CudaException.ThrowIfError(
                    CudaDriverApi.cuMemcpyHtoD_v2(DevicePointer, handle.AddrOfPinnedObject(),
                        hostData.Length * 4));
            }
            finally
            {
                handle.Free();
            }
        }
        else
        {
            if (!NativeMethods.GpuBigInt_CopyHostToDevice(DevicePointer, hostData, NumWords))
            {
                throw new InvalidOperationException(
                    $"Failed to copy host data ({hostData.Length} words) to GPU device pointer 0x{DevicePointer:X} (expected {NumWords} words).");
            }
        }
    }

    public GpuBigInteger Evaluate(GpuContext context, GpuBigInteger? targetResult = null)
    {
        if (targetResult == null || targetResult == this)
        {
            return this;
        }

        targetResult.CopyFrom(this);
        return targetResult;
    }

    public GpuBigIntExpr Value
    {
        get => new(this);
        set => value.Node.Evaluate(this.Context, this);
    }

    public static GpuBigIntExpr operator +(GpuBigInteger a, GpuBigInteger b) => new(new AddExpression(a, b));
    public static GpuBigIntExpr operator -(GpuBigInteger a, GpuBigInteger b) => new(new SubExpression(a, b));
    public static GpuBigIntExpr operator &(GpuBigInteger a, GpuBigInteger b) => new(new BitwiseAndExpression(a, b));

    public static GpuBigIntExpr operator <<(GpuBigInteger a, int shiftBits) =>
        new(new ShiftLeftExpression(a, shiftBits));

    public static GpuBigIntExpr operator >> (GpuBigInteger a, int shiftBits) =>
        new(new ShiftRightExpression(a, shiftBits));

    public uint[] ToHostArray()
    {
        var hostData = new uint[NumWords];
        if (Context.Engine == GpuExecutionEngine.GSharpJit)
        {
            var handle = GCHandle.Alloc(hostData, GCHandleType.Pinned);
            try
            {
                CudaException.ThrowIfError(
                    CudaDriverApi.cuMemcpyDtoH_v2(handle.AddrOfPinnedObject(), DevicePointer,
                        NumWords * 4));
            }
            finally
            {
                handle.Free();
            }
        }
        else
        {
            if (!NativeMethods.GpuBigInt_CopyDeviceToHost(hostData, DevicePointer, NumWords))
            {
                throw new InvalidOperationException(
                    $"Failed to copy {NumWords} words from GPU device pointer 0x{DevicePointer:X} to host.");
            }
        }

        return hostData;
    }

    public void CopyFrom(GpuBigInteger source)
    {
        if (source.NumWords != NumWords)
        {
            throw new ArgumentException(
                $"Size mismatch between GPU buffers: source has {source.NumWords} words, but target has {NumWords} words.",
                nameof(source));
        }

        if (Context.Engine == GpuExecutionEngine.GSharpJit)
        {
            CudaException.ThrowIfError(
                CudaDriverApi.cuMemcpyDtoD_v2(DevicePointer, source.DevicePointer, NumWords * 4));
        }
        else
        {
            if (!NativeMethods.GpuBigInt_CopyDeviceToDevice(DevicePointer, source.DevicePointer, NumWords))
            {
                throw new InvalidOperationException(
                    $"Failed to copy {NumWords} words on GPU from 0x{source.DevicePointer:X} to 0x{DevicePointer:X}.");
            }
        }
    }

    public static void BitwiseAnd(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        if (a.NumWords != b.NumWords || a.NumWords != result.NumWords)
        {
            throw new ArgumentException(
                $"All buffers must have the same size. a: {a.NumWords}, b: {b.NumWords}, result: {result.NumWords} words.");
        }

        if (a.Context.Engine == GpuExecutionEngine.GSharpJit)
        {
            const uint BlockSize = 256;
            var gridSize = (a.NumWords + BlockSize - 1) / BlockSize;
            if (gridSize == 0)
            {
                gridSize = 1;
            }

            GpuBigIntKernelsWrapper.Launch_BitwiseAndKernel_Device(
                a.DevicePointer, b.DevicePointer, result.DevicePointer, a.NumWords,
                gridDimX: (uint)gridSize, blockDimX: BlockSize);
        }
        else
        {
            NativeMethods.GpuBigInt_BitwiseAnd(a.Context.WorkspacePointer, a.DevicePointer, b.DevicePointer,
                result.DevicePointer, a.NumWords);
        }
    }

    public static void Add(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        if (a.NumWords != b.NumWords || a.NumWords != result.NumWords)
        {
            throw new ArgumentException(
                $"All buffers must have the same size. a: {a.NumWords}, b: {b.NumWords}, result: {result.NumWords} words.");
        }

        if (a.Context.Engine == GpuExecutionEngine.GSharpJit)
        {
            var chunkSize = 256;
            var numChunks = (a.NumWords + chunkSize - 1) / chunkSize;
            uint blockSize = 256;
            var gridSize = (numChunks + blockSize - 1) / blockSize;
            if (gridSize == 0)
            {
                gridSize = 1;
            }

            var carryBufferSize = (numChunks + 1) * sizeof(long);
            var carryBuffer = a.Context.CarryBuffer;
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(carryBuffer, 0, (IntPtr)carryBufferSize));

            GpuBigIntKernelsWrapper.Launch_ChunkAddKernel_Device(
                a.DevicePointer, b.DevicePointer, result.DevicePointer, carryBuffer, a.NumWords, chunkSize,
                gridDimX: (uint)gridSize, blockDimX: blockSize);

            GpuBigIntKernelsWrapper.Launch_ApplyAddCarryKernel_Device(
                result.DevicePointer, carryBuffer, numChunks, a.NumWords, chunkSize);
        }
        else
        {
            NativeMethods.GpuBigInt_Add(a.Context.WorkspacePointer, a.DevicePointer, b.DevicePointer,
                result.DevicePointer,
                a.NumWords);
        }
    }

    public static void Sub(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
    {
        if (a.NumWords != b.NumWords || a.NumWords != result.NumWords)
        {
            throw new ArgumentException(
                $"All buffers must have the same size. a: {a.NumWords}, b: {b.NumWords}, result: {result.NumWords} words.");
        }

        if (a.Context.Engine == GpuExecutionEngine.GSharpJit)
        {
            var chunkSize = 256;
            var numChunks = (a.NumWords + chunkSize - 1) / chunkSize;
            uint blockSize = 256;
            var gridSize = (numChunks + blockSize - 1) / blockSize;
            if (gridSize == 0)
            {
                gridSize = 1;
            }

            var carryBufferSize = (numChunks + 1) * sizeof(long);
            var carryBuffer = a.Context.CarryBuffer;
            CudaException.ThrowIfError(CudaDriverApi.cuMemsetD8_v2(carryBuffer, 0, (IntPtr)carryBufferSize));

            GpuBigIntKernelsWrapper.Launch_ChunkSubKernel_Device(
                a.DevicePointer, b.DevicePointer, result.DevicePointer, carryBuffer, a.NumWords, chunkSize,
                gridDimX: (uint)gridSize, blockDimX: blockSize);

            GpuBigIntKernelsWrapper.Launch_ApplySubBorrowKernel_Device(
                result.DevicePointer, carryBuffer, numChunks, a.NumWords, chunkSize);
        }
        else
        {
            NativeMethods.GpuBigInt_Sub(a.Context.WorkspacePointer, a.DevicePointer, b.DevicePointer,
                result.DevicePointer,
                a.NumWords);
        }
    }

    public static void ShiftLeft(GpuBigInteger a, int shiftBits, GpuBigInteger result)
    {
        if (a.NumWords != result.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must have the same size as input buffer. a: {a.NumWords}, result: {result.NumWords} words.");
        }

        if (a.Context.Engine == GpuExecutionEngine.GSharpJit)
        {
            var wordShift = shiftBits / 32;
            var bitShift = shiftBits % 32;
            uint blockSize = 256;
            var gridSize = (a.NumWords + blockSize - 1) / blockSize;
            if (gridSize == 0)
            {
                gridSize = 1;
            }

            GpuBigIntKernelsWrapper.Launch_ShiftLeftKernel_Device(
                a.DevicePointer, wordShift, bitShift, result.DevicePointer, a.NumWords,
                gridDimX: (uint)gridSize, blockDimX: blockSize);
        }
        else
        {
            NativeMethods.GpuBigInt_ShiftLeft(a.Context.WorkspacePointer, a.DevicePointer, shiftBits,
                result.DevicePointer,
                a.NumWords);
        }
    }

    public static void ShiftRight(GpuBigInteger a, int shiftBits, GpuBigInteger result)
    {
        if (a.NumWords != result.NumWords)
        {
            throw new ArgumentException(
                $"Result buffer must have the same size as input buffer. a: {a.NumWords}, result: {result.NumWords} words.");
        }

        if (a.Context.Engine == GpuExecutionEngine.GSharpJit)
        {
            var wordShift = shiftBits / 32;
            var bitShift = shiftBits % 32;
            uint blockSize = 256;
            var gridSize = (a.NumWords + blockSize - 1) / blockSize;
            if (gridSize == 0)
            {
                gridSize = 1;
            }

            GpuBigIntKernelsWrapper.Launch_ShiftRightKernel_Device(
                a.DevicePointer, wordShift, bitShift, result.DevicePointer, a.NumWords,
                gridDimX: (uint)gridSize, blockDimX: blockSize);
        }
        else
        {
            NativeMethods.GpuBigInt_ShiftRight(a.Context.WorkspacePointer, a.DevicePointer, shiftBits,
                result.DevicePointer,
                a.NumWords);
        }
    }

    public bool Square(int logicalWords) =>
        NativeMethods.GpuBigInt_Square(Context.WorkspacePointer, DevicePointer, logicalWords);

    internal void DisposeCore()
    {
        if (!_disposed)
        {
            if (DevicePointer != IntPtr.Zero)
            {
                if (Context.Engine == GpuExecutionEngine.GSharpJit)
                {
                    CudaDriverApi.cuMemFree_v2(DevicePointer);
                }
                else
                {
                    NativeMethods.GpuBigInt_Free(DevicePointer);
                }

                DevicePointer = IntPtr.Zero;
            }

            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (IsTemp)
        {
            Context.ReturnTemp(this);
            return;
        }

        DisposeCore();
    }

    ~GpuBigInteger()
    {
        Dispose();
    }
}
