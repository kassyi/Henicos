namespace Kassyi.GSharp.Stdlib.Math.BigFloat.Runtime;

using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;

public interface IGpuBigFloatArithmetic
{
    void Add(ref BigIntExecutionContext context, 
             BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
             BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
             BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    void Multiply(ref BigIntExecutionContext context, 
                  BigIntMemoryHandle leftSig, int leftExp, bool leftSign,
                  BigIntMemoryHandle rightSig, int rightExp, bool rightSign,
                  BigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
}
