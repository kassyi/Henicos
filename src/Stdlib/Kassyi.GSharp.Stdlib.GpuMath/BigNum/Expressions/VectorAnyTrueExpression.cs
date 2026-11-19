namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorAnyTrueExpression([TakesOwnership] IGpuBoolVectorExpression source) : IGpuBoolExpr
{
    // Whether the element exceptions are recorded, decided once: the expression does not change after it is built
    private readonly bool _defers = BoolVectorQuantifier.Defers(source);

    public bool Evaluate(GpuWorkspace workspace) =>
        BoolVectorQuantifier.Evaluate(workspace, source, all: false, _defers);

    public void Dispose() => source.Dispose();
}
