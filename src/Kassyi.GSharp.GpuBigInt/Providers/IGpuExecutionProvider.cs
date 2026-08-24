namespace Kassyi.GSharp.GpuBigInt.Providers;

public interface IGpuExecutionProvider
{
    IntPtr AllocateMemory(int numWords);
    void FreeMemory(IntPtr devicePointer);
    void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords);
    void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords);
    void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords);

    void Add(GpuContext context, GpuBigInteger a, GpuBigInteger b, GpuBigInteger result);
    void Sub(GpuContext context, GpuBigInteger a, GpuBigInteger b, GpuBigInteger result);
    void Mul(GpuContext context, GpuBigInteger a, GpuBigInteger b, GpuBigInteger result);
    void BitwiseAnd(GpuContext context, GpuBigInteger a, GpuBigInteger b, GpuBigInteger result);
    void ShiftLeft(GpuContext context, GpuBigInteger a, int shiftBits, GpuBigInteger result);
    void ShiftRight(GpuContext context, GpuBigInteger a, int shiftBits, GpuBigInteger result);
    bool Square(GpuContext context, IntPtr devicePointer, int logicalWords);
}

