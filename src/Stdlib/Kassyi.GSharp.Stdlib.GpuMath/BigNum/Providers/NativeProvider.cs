using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Execution provider implementing arbitrary-precision arithmetic via pre-compiled native CUDA C++ dynamic libraries.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Interops directly with native C++/CUDA shared libraries (`GpuBigInt.dll` / `libGpuBigInt.so`)
/// for legacy benchmark comparison and high-performance native math library integration.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> Direct P/Invoke bindings communicate with unmanaged GPU memory without managed array allocation overhead.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage]
public class NativeProvider : IGpuExecutionProvider
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="NativeProvider"/>.
    /// </summary>
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

        if (!NativeMethods.GpuBigInt_ClearMemory(devicePointer, numWords, stream))
        {
            throw new InvalidOperationException(
                $"Failed to clear memory on GPU device pointer 0x{devicePointer.ToInt64():X}.");
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

    /// <inheritdoc/>
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
    public void SynchronizeStream(IntPtr stream)
    {
        // No-op for legacy native synchronous calls
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
    public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) =>
        NativeMethods.GpuBigInt_Add(workspace.WorkspacePointer, leftOperand.DevicePointer, rightOperand.DevicePointer,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <inheritdoc/>
    public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) =>
        NativeMethods.GpuBigInt_Sub(workspace.WorkspacePointer, leftOperand.DevicePointer, rightOperand.DevicePointer,
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
    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void InverseSqrtMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int actualBits, int iterations) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void ReciprocalMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int numBits, int marginBits, int iterations, int targetBits) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc />
    public void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer)
        => NativeMethods.GpuBigInt_BitwiseAnd(workspace.WorkspacePointer, leftOperand.DevicePointer,
            rightOperand.DevicePointer,
            resultBuffer.DevicePointer, leftOperand.NumWords);

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc/>
    public void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer) =>
        NativeMethods.GpuBigInt_ShiftLeft(workspace.WorkspacePointer, leftOperand.DevicePointer, shiftBits,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <inheritdoc/>
    public void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer) =>
        NativeMethods.GpuBigInt_ShiftRight(workspace.WorkspacePointer, leftOperand.DevicePointer, shiftBits,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <inheritdoc/>
    public bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords) =>
        NativeMethods.GpuBigInt_Square(workspace.WorkspacePointer, devicePointer, logicalWords);

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand)
    {
        var words = new uint[leftOperand.NumWords];
        CopyDeviceToHost(words, leftOperand.DevicePointer, leftOperand.NumWords);
        return words.AsStackEnumerable().All(t => t == 0);
    }

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand) => throw new NotImplementedException();

    /// <inheritdoc/>
    public int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand)
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

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) => throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc />
    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc/>
    public void BeginCapture(IntPtr stream) => throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc/>
    public IntPtr EndCapture(IntPtr stream) => throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc/>
    public IntPtr InstantiateGraph(IntPtr graph) => throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc/>
    public void LaunchGraph(IntPtr graphExec, IntPtr stream) => throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc/>
    public void DestroyGraph(IntPtr graph, IntPtr graphExec) => throw new NotImplementedException(ErrorNotImplemented);
}








