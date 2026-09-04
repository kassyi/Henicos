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

    public void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords)
    {
        if (!NativeMethods.GpuBigInt_CopyHostToDevice(devicePointer, hostData, numWords))
        {
            throw new InvalidOperationException(string.Format(ErrorCopyHostToDeviceFailed, devicePointer));
        }
    }

    /// <inheritdoc/>

    public void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords)
    {
        if (!NativeMethods.GpuBigInt_CopyDeviceToHost(hostData, devicePointer, numWords))
        {
            throw new InvalidOperationException(string.Format(ErrorCopyDeviceToHostFailed, devicePointer));
        }
    }

    /// <inheritdoc/>

    public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords)
    {
        if (!NativeMethods.GpuBigInt_CopyDeviceToDevice(destPointer, srcPointer, numWords))
        {
            throw new InvalidOperationException(string.Format(ErrorCopyDeviceToDeviceFailed, srcPointer, destPointer));
        }
    }

    /// <inheritdoc/>

    public void Add(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_Add(context.WorkspacePointer, leftOperand.DevicePointer, rightOperand.DevicePointer,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <inheritdoc/>

    public void Sub(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_Sub(context.WorkspacePointer, leftOperand.DevicePointer, rightOperand.DevicePointer,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Mul(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Add(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Sub(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Mul(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc/>

    public void BitwiseAnd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
        => NativeMethods.GpuBigInt_BitwiseAnd(context.WorkspacePointer, leftOperand.DevicePointer,
            rightOperand.DevicePointer,
            resultBuffer.DevicePointer, leftOperand.NumWords);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void BitwiseOr(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void BitwiseXor(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void BitwiseNot(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc/>

    public void ShiftLeft(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_ShiftLeft(context.WorkspacePointer, leftOperand.DevicePointer, shiftBits,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <inheritdoc/>

    public void ShiftRight(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_ShiftRight(context.WorkspacePointer, leftOperand.DevicePointer, shiftBits,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <inheritdoc/>

    public bool Square(GpuContext context, IntPtr devicePointer, int logicalWords) =>
        NativeMethods.GpuBigInt_Square(context.WorkspacePointer, devicePointer, logicalWords);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public bool IsZero(GpuContext context, GpuBigInteger leftOperand) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public bool IsOne(GpuContext context, GpuBigInteger leftOperand) => throw new NotImplementedException();

    /// <inheritdoc/>

    public int GetBitLength(GpuContext context, GpuBigInteger leftOperand)
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
    public int CompareTo(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Gcd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) => throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc/>
    public void ClearCaches()
    {
        // No caching implemented in the native provider.
    }
}





