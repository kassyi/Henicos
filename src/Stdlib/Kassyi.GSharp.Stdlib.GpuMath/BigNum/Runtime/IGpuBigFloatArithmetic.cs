namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Runtime;

public interface IGpuBigFloatArithmetic
{
    void Normalize(ref GpuBigIntExecutionContext context,
                   GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
                   GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    void Add(ref GpuBigIntExecutionContext context,
             GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
             GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
             GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    void Multiply(ref GpuBigIntExecutionContext context,
                  GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
                  GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
                  GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    void Divide(ref GpuBigIntExecutionContext context,
                GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
                GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
                GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    void Reciprocal(ref GpuBigIntExecutionContext context,
                    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
                    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    void Sqrt(ref GpuBigIntExecutionContext context,
              GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
              GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    void Rsqrt(ref GpuBigIntExecutionContext context,
               GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
               GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    void Exp(ref GpuBigIntExecutionContext context,
             GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
             GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    void Log(ref GpuBigIntExecutionContext context,
             GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
             GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    void Pow(ref GpuBigIntExecutionContext context,
             GpuBigIntMemoryHandle baseSig, int baseExp, bool baseSign,
             GpuBigIntMemoryHandle expSig, int exponentExp, bool exponentSign,
             GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);

    void DivideByUInt32(ref GpuBigIntExecutionContext context,
                        GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
                        uint divisor,
                        GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign, out uint remainder);
}
