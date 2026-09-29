using Kassyi.GSharp.Stdlib.Math.Interop;

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Providers;

/// <summary>
/// Execution provider implementing arbitrary-precision arithmetic via pre-compiled native CUDA C++ dynamic libraries.
/// </summary>
public class NativeProvider : IGpuExecutionProvider
{
    /// <summary>Gets the singleton instance of the <see cref="NativeProvider"/>.</summary>
    public static readonly NativeProvider Instance = new();

    private const string ErrorNotImplemented =
        "This operation is not supported by the NativeProvider. Use GSharp JitProvider instead.";

    private const string ErrorCopyHostToDeviceFailed = "Failed to copy host data to GPU device pointer 0x{0:X}.";
    private const string ErrorCopyDeviceToHostFailed = "Failed to copy data from GPU device pointer 0x{0:X} to host.";
    private const string ErrorCopyDeviceToDeviceFailed = "Failed to copy data on GPU from 0x{0:X} to 0x{1:X}.";

    private NativeProvider()
    {
    }

    /// <inheritdoc/>

        public void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream)
    {
        if (devicePointer == IntPtr.Zero || numWords <= 0)
        {
            return;
        }

        var zeroArray = System.Buffers.ArrayPool<uint>.Shared.Rent(numWords);
        try
        {
            Array.Clear(zeroArray, 0, numWords);
            CopyHostToDevice(devicePointer, zeroArray, numWords, stream);
        }
        finally
        {
            System.Buffers.ArrayPool<uint>.Shared.Return(zeroArray);
        }
    }

    /// <inheritdoc/>
    public IntPtr AllocateMemory(int numWords) => NativeMethods.GpuBigInt_Alloc(numWords);

    /// <inheritdoc/>

    public void FreeMemory(IntPtr devicePointer)
    {
        if (devicePointer != IntPtr.Zero)
        {
            NativeMethods.GpuBigInt_Free(devicePointer);
        }
    }

    /// <inheritdoc/>

    public void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0)
    {
        if (!NativeMethods.GpuBigInt_CopyHostToDevice(devicePointer, hostData, numWords))
        {
            throw new InvalidOperationException(string.Format(ErrorCopyHostToDeviceFailed, devicePointer));
        }
    }

    public void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0)
    {
        if (!NativeMethods.GpuBigInt_CopyDeviceToHost(hostData, devicePointer, numWords))
        {
            throw new InvalidOperationException(string.Format(ErrorCopyDeviceToHostFailed, devicePointer));
        }
    }

    /// <inheritdoc/>
    public uint ReadWord(IntPtr devicePointer, int wordOffset = 0)
    {
        var temp = new uint[1];
        CopyDeviceToHost(temp, IntPtr.Add(devicePointer, wordOffset * 4), 1);
        return temp[0];
    }

    /// <inheritdoc/>
    public void WriteWord(IntPtr devicePointer, int wordOffset, uint value)
    {
        var temp = new[] { value };
        CopyHostToDevice(IntPtr.Add(devicePointer, wordOffset * 4), temp, 1);
    }

    /// <inheritdoc/>

    public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0)
    {
        if (!NativeMethods.GpuBigInt_CopyDeviceToDevice(destPointer, srcPointer, numWords))
        {
            throw new InvalidOperationException(string.Format(ErrorCopyDeviceToDeviceFailed, srcPointer, destPointer));
        }
    }

    /// <inheritdoc/>

    public void Add(GpuWorkspace context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_Add(context.WorkspacePointer, leftOperand.DevicePointer, rightOperand.DevicePointer,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <inheritdoc/>

    public void Sub(GpuWorkspace context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_Sub(context.WorkspacePointer, leftOperand.DevicePointer, rightOperand.DevicePointer,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /*
     * DESIGN RATIONALE (Out-of-Core Architecture & Arbitrary Precision Scaling):
     * Even on high-end hardware, GPU VRAM (16GB-80GB) is comparable in scale to host RAM (32GB-128GB).
     * Attempting to execute monolithic multi-million-limb multiplication entirely inside a single GPU kernel
     * would inevitably hit hardware VRAM ceilings and fail for astronomical numbers.
     *
     * G# is architected to handle virtually infinite precision by leveraging the host CPU and disk storage
     * as an unbounded "scratchpad" (Out-of-Core computation). The host orchestrates the high-level computation
     * recipe (AST / expression visitor, NTT block transformations, chunking, and disk streaming), while the GPU
     * acts as an ultra-high-speed acceleration assistant for fine-grained parallel primitives (Square, Add, Sub, etc.).
     * Even if monolithic single-kernel dispatch incurs slightly less kernel-launch overhead, sacrificing that in favor
     * of host-orchestrated chunked execution is an intentional, architectural decision to scale beyond physical VRAM limits.
     */
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Mul(GpuWorkspace context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void InverseSqrtMega(GpuWorkspace context, GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer, int marginWords, int actualBits, int iterations) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void ReciprocalMega(GpuWorkspace context, GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer, int marginWords, int numBits, int marginBits, int iterations, int targetBits) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Add(GpuWorkspace context, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Sub(GpuWorkspace context, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Mul(GpuWorkspace context, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc/>

    public void BitwiseAnd(GpuWorkspace context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
        => NativeMethods.GpuBigInt_BitwiseAnd(context.WorkspacePointer, leftOperand.DevicePointer,
            rightOperand.DevicePointer,
            resultBuffer.DevicePointer, leftOperand.NumWords);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void BitwiseOr(GpuWorkspace context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void BitwiseXor(GpuWorkspace context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void BitwiseNot(GpuWorkspace context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc/>

    public void ShiftLeft(GpuWorkspace context, GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_ShiftLeft(context.WorkspacePointer, leftOperand.DevicePointer, shiftBits,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <inheritdoc/>

    public void ShiftRight(GpuWorkspace context, GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_ShiftRight(context.WorkspacePointer, leftOperand.DevicePointer, shiftBits,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <inheritdoc/>

    public bool Square(GpuWorkspace context, IntPtr devicePointer, int logicalWords) =>
        NativeMethods.GpuBigInt_Square(context.WorkspacePointer, devicePointer, logicalWords);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public bool IsZero(GpuWorkspace context, GpuUIntBigInteger leftOperand) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public bool IsOne(GpuWorkspace context, GpuUIntBigInteger leftOperand) => throw new NotImplementedException();

    /// <inheritdoc/>

    public int GetBitLength(GpuWorkspace context, GpuUIntBigInteger leftOperand)
    {
        var words = new uint[leftOperand.NumWords];
        CopyDeviceToHost(words, leftOperand.DevicePointer, leftOperand.NumWords);
        for (var i = words.Length - 1; i >= 0; i--)
        {
            if (words[i] != 0)
            {
                return i * 32 + (32 - BitOperations.LeadingZeroCount(words[i]));
            }
        }

        return 0;
    }

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public int CompareTo(GpuWorkspace context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Gcd(GpuWorkspace context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) => throw new NotImplementedException(ErrorNotImplemented);

    public void BeginCapture(IntPtr stream) => throw new NotImplementedException(ErrorNotImplemented);
    public IntPtr EndCapture(IntPtr stream) => throw new NotImplementedException(ErrorNotImplemented);
    public IntPtr InstantiateGraph(IntPtr graph) => throw new NotImplementedException(ErrorNotImplemented);
    public void LaunchGraph(IntPtr graphExec, IntPtr stream) => throw new NotImplementedException(ErrorNotImplemented);
    public void DestroyGraph(IntPtr graph, IntPtr graphExec) => throw new NotImplementedException(ErrorNotImplemented);
}







