namespace Kassyi.GSharp.Stdlib.GpuMath.BigInt.Runtime;

public interface IGpuBigIntArithmetic
{
    void Add(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    void Sub(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    void Mul(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    void Div(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    void Mod(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    void BitwiseAnd(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    void BitwiseOr(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    void BitwiseXor(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle left, GpuBigIntMemoryHandle right,
        GpuBigIntMemoryHandle result);

    void BitwiseNot(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target, GpuBigIntMemoryHandle result);

    void ShiftLeft(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target, int shiftBits,
        GpuBigIntMemoryHandle result);

    void ShiftRight(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target, int shiftBits,
        GpuBigIntMemoryHandle result);

    void Sqrt(ref GpuBigIntExecutionContext context, GpuBigIntMemoryHandle target, GpuBigIntMemoryHandle result);
}
