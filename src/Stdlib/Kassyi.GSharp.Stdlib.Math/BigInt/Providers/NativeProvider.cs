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

    public void Add(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_Add(context.WorkspacePointer, leftOperand.DevicePointer, rightOperand.DevicePointer,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <inheritdoc/>

    public void Sub(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_Sub(context.WorkspacePointer, leftOperand.DevicePointer, rightOperand.DevicePointer,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Mul(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void InverseSqrtMega(GpuContext context, GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer, int marginWords, int actualBits, int iterations) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void ReciprocalMega(GpuContext context, GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer, int marginWords, int numBits, int marginBits, int iterations) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Add(GpuContext context, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Sub(GpuContext context, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Mul(GpuContext context, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc/>

    public void BitwiseAnd(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer)
        => NativeMethods.GpuBigInt_BitwiseAnd(context.WorkspacePointer, leftOperand.DevicePointer,
            rightOperand.DevicePointer,
            resultBuffer.DevicePointer, leftOperand.NumWords);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void BitwiseOr(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void BitwiseXor(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void BitwiseNot(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <inheritdoc/>

    public void ShiftLeft(GpuContext context, GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_ShiftLeft(context.WorkspacePointer, leftOperand.DevicePointer, shiftBits,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <inheritdoc/>

    public void ShiftRight(GpuContext context, GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer) =>
        NativeMethods.GpuBigInt_ShiftRight(context.WorkspacePointer, leftOperand.DevicePointer, shiftBits,
            resultBuffer.DevicePointer,
            leftOperand.NumWords);

    /// <inheritdoc/>

    public bool Square(GpuContext context, IntPtr devicePointer, int logicalWords) =>
        NativeMethods.GpuBigInt_Square(context.WorkspacePointer, devicePointer, logicalWords);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public bool IsZero(GpuContext context, GpuUIntBigInteger leftOperand) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public bool IsOne(GpuContext context, GpuUIntBigInteger leftOperand) => throw new NotImplementedException();

    /// <inheritdoc/>

    public int GetBitLength(GpuContext context, GpuUIntBigInteger leftOperand)
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
    public int CompareTo(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        throw new NotImplementedException(ErrorNotImplemented);

    /// <exception cref="NotImplementedException">Not supported on the native backend.</exception>
    public void Gcd(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) => throw new NotImplementedException(ErrorNotImplemented);

    public void BeginCapture(IntPtr stream) => throw new NotImplementedException(ErrorNotImplemented);
    public IntPtr EndCapture(IntPtr stream) => throw new NotImplementedException(ErrorNotImplemented);
    public IntPtr InstantiateGraph(IntPtr graph) => throw new NotImplementedException(ErrorNotImplemented);
    public void LaunchGraph(IntPtr graphExec, IntPtr stream) => throw new NotImplementedException(ErrorNotImplemented);
    public void DestroyGraph(IntPtr graph, IntPtr graphExec) => throw new NotImplementedException(ErrorNotImplemented);
}





