using Kassyi.GSharp.Stdlib.Math.Interop;

namespace Kassyi.GSharp.Stdlib.Math.Providers;

public class NativeProvider : IGpuExecutionProvider
{
    public static readonly NativeProvider Instance = new();

    private const string ErrorNotImplemented =
        "This operation is not supported by the NativeProvider. Use GSharp JitProvider instead.";

    private const string ErrorCopyHostToDeviceFailed = "Failed to copy host data to GPU device pointer 0x{0:X}.";
    private const string ErrorCopyDeviceToHostFailed = "Failed to copy data from GPU device pointer 0x{0:X} to host.";
    private const string ErrorCopyDeviceToDeviceFailed = "Failed to copy data on GPU from 0x{0:X} to 0x{1:X}.";

    private NativeProvider()
    {
    }

    public IntPtr AllocateMemory(int numWords) => NativeMethods.GpuBigInt_Alloc(numWords);

    public void FreeMemory(IntPtr devicePointer)
    {
        if (devicePointer != IntPtr.Zero)
        {
            NativeMethods.GpuBigInt_Free(devicePointer);
        }
    }

    public void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords)
    {
        if (!NativeMethods.GpuBigInt_CopyHostToDevice(devicePointer, hostData, numWords))
        {
            throw new InvalidOperationException(string.Format(ErrorCopyHostToDeviceFailed, devicePointer));
        }
    }

    public void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords)
    {
        if (!NativeMethods.GpuBigInt_CopyDeviceToHost(hostData, devicePointer, numWords))
        {
            throw new InvalidOperationException(string.Format(ErrorCopyDeviceToHostFailed, devicePointer));
        }
    }

    public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords)
    {
        if (!NativeMethods.GpuBigInt_CopyDeviceToDevice(destPointer, srcPointer, numWords))
        {
            throw new InvalidOperationException(string.Format(ErrorCopyDeviceToDeviceFailed, srcPointer, destPointer));
        }
    }

    public void Add(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_Add(context.WorkspacePointer, leftOperand.DevicePointer, rightOperand.DevicePointer,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    public void Sub(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_Sub(context.WorkspacePointer, leftOperand.DevicePointer, rightOperand.DevicePointer,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    public void Mul(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    public void Add(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    public void Sub(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    public void Mul(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    public void BitwiseAnd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer)
        => NativeMethods.GpuBigInt_BitwiseAnd(context.WorkspacePointer, leftOperand.DevicePointer,
            rightOperand.DevicePointer,
            resultBuffer.DevicePointer, leftOperand.NumWords);

    public void BitwiseOr(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    public void BitwiseXor(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    public void BitwiseNot(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    public void ShiftLeft(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_ShiftLeft(context.WorkspacePointer, leftOperand.DevicePointer, shiftBits,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    public void ShiftRight(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_ShiftRight(context.WorkspacePointer, leftOperand.DevicePointer, shiftBits,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    public bool Square(GpuContext context, IntPtr devicePointer, int logicalWords) =>
        NativeMethods.GpuBigInt_Square(context.WorkspacePointer, devicePointer, logicalWords);

    public bool IsZero(GpuContext context, GpuBigInteger leftOperand) =>
        throw new NotImplementedException(ErrorNotImplemented);

    public bool IsOne(GpuContext context, GpuBigInteger leftOperand) =>
        throw new NotImplementedException(ErrorNotImplemented);

    public int CompareTo(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand) =>
        throw new NotImplementedException(ErrorNotImplemented);

    public void Gcd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand,
        GpuBigInteger resultBuffer) => throw new NotImplementedException(ErrorNotImplemented);
}




