namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorAnyTrueExpression([TakesOwnership] IGpuBoolVectorExpression source) : IGpuBoolExpr
{
    public bool Evaluate(GpuWorkspace workspace) => BoolVectorQuantifier.Evaluate(workspace, source, all: false);

    public void Dispose() => source.Dispose();
}
