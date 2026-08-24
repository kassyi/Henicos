namespace Kassyi.GSharp.GpuBigInt.Providers;

public interface IGpuExecutionProvider
{
    IntPtr AllocateMemory(int numWords);
    void FreeMemory(IntPtr devicePointer);
    void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords);
    void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords);
    void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords);

    void Add(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);
    void Sub(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);
    void Mul(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);

    void Add(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer);
    void Sub(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer);
    void Mul(GpuContext context, GpuBigInteger leftOperand, uint rightOperand, GpuBigInteger resultBuffer);
    void BitwiseAnd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);
    void BitwiseOr(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);
    void BitwiseXor(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);
    void BitwiseNot(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger resultBuffer);
    void ShiftLeft(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer);
    void ShiftRight(GpuContext context, GpuBigInteger leftOperand, int shiftBits, GpuBigInteger resultBuffer);
    bool Square(GpuContext context, IntPtr devicePointer, int logicalWords);
    void Gcd(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand, GpuBigInteger resultBuffer);

    bool IsZero(GpuContext context, GpuBigInteger leftOperand);
    bool IsOne(GpuContext context, GpuBigInteger leftOperand);
    int CompareTo(GpuContext context, GpuBigInteger leftOperand, GpuBigInteger rightOperand);
}


