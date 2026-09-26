namespace Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

public interface IGpuBigIntArithmetic
{
    void Add(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result);
    void Sub(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result);
    void Mul(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result);
    void Div(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result);
    void BitwiseAnd(ref BigIntExecutionContext context, BigIntMemoryHandle left, BigIntMemoryHandle right, BigIntMemoryHandle result);
    void BitwiseNot(ref BigIntExecutionContext context, BigIntMemoryHandle target, BigIntMemoryHandle result);
    void ShiftLeft(ref BigIntExecutionContext context, BigIntMemoryHandle target, int shiftBits, BigIntMemoryHandle result);
    void ShiftRight(ref BigIntExecutionContext context, BigIntMemoryHandle target, int shiftBits, BigIntMemoryHandle result);
    void Sqrt(ref BigIntExecutionContext context, BigIntMemoryHandle target, BigIntMemoryHandle result);
}
