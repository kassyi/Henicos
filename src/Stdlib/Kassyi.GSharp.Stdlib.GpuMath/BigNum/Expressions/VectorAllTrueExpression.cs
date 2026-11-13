namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorAllTrueExpression([TakesOwnership] IGpuBoolVectorExpression source) : IGpuBoolExpr
{
    public bool Evaluate(GpuWorkspace workspace) => BoolVectorQuantifier.Evaluate(workspace, source, all: true);

    public void Dispose() => source.Dispose();
}
