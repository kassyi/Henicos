namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public interface IGpuBoolVectorExpression : System.IDisposable
{
    int Length { get; }
    GpuBigUIntArray Evaluate(GpuWorkspace workspace);
}
