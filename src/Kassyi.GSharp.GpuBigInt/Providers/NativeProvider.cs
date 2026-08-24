using Kassyi.GSharp.GpuBigInt.Interop;

namespace Kassyi.GSharp.GpuBigInt.Providers;

public class NativeProvider : IGpuExecutionProvider
{
    public static readonly NativeProvider Instance = new();

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
            throw new InvalidOperationException(
                $"Failed to copy host data to GPU device pointer 0x{devicePointer:X}.");
        }
    }

    public void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords)
    {
        if (!NativeMethods.GpuBigInt_CopyDeviceToHost(hostData, devicePointer, numWords))
        {
            throw new InvalidOperationException(
                $"Failed to copy data from GPU device pointer 0x{devicePointer:X} to host.");
        }
    }

    public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords)
    {
        if (!NativeMethods.GpuBigInt_CopyDeviceToDevice(destPointer, srcPointer, numWords))
        {
            throw new InvalidOperationException(
                $"Failed to copy data on GPU from 0x{srcPointer:X} to 0x{destPointer:X}.");
        }
    }

    public void Add(GpuContext context, GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) =>
        NativeMethods.GpuBigInt_Add(context.WorkspacePointer, a.DevicePointer, b.DevicePointer, result.DevicePointer,
            a.NumWords);

    public void Sub(GpuContext context, GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) =>
        NativeMethods.GpuBigInt_Sub(context.WorkspacePointer, a.DevicePointer, b.DevicePointer, result.DevicePointer,
            a.NumWords);

    public void Mul(GpuContext context, GpuBigInteger a, GpuBigInteger b, GpuBigInteger result) =>
        throw new NotImplementedException(
            "Generic multiplication (Mul) is not implemented in the legacy NativeProvider. Use GSharp JitProvider instead.");

    public void BitwiseAnd(GpuContext context, GpuBigInteger a, GpuBigInteger b, GpuBigInteger result)
        => NativeMethods.GpuBigInt_BitwiseAnd(context.WorkspacePointer, a.DevicePointer, b.DevicePointer,
            result.DevicePointer, a.NumWords);

    public void ShiftLeft(GpuContext context, GpuBigInteger a, int shiftBits, GpuBigInteger result) =>
        NativeMethods.GpuBigInt_ShiftLeft(context.WorkspacePointer, a.DevicePointer, shiftBits, result.DevicePointer,
            a.NumWords);

    public void ShiftRight(GpuContext context, GpuBigInteger a, int shiftBits, GpuBigInteger result) =>
        NativeMethods.GpuBigInt_ShiftRight(context.WorkspacePointer, a.DevicePointer, shiftBits, result.DevicePointer,
            a.NumWords);

    public bool Square(GpuContext context, IntPtr devicePointer, int logicalWords) =>
        NativeMethods.GpuBigInt_Square(context.WorkspacePointer, devicePointer, logicalWords);
}

