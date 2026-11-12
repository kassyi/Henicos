namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;
using Providers;

internal sealed class FloatVectorUnaryExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    FloatVectorUnaryOpKind opKind) : FloatVectorUnaryExpressionBase(vector)
{
    // Every kind runs through the host arithmetic, which throws for an element outside its domain or an exponent that
    // overflows
    protected override bool ElementMayThrow => true;

    protected override GpuFloatResult EvaluateElement(
        ref GpuBigIntExecutionContext context,
        IGpuBigFloatArithmetic arithmetic,
        in GpuFloatOperand source,
        GpuBigIntMemoryHandle destination) =>
        opKind switch
        {
            FloatVectorUnaryOpKind.Sqrt => arithmetic.Sqrt(ref context, in source, destination),
            FloatVectorUnaryOpKind.Rsqrt => arithmetic.Rsqrt(ref context, in source, destination),
            FloatVectorUnaryOpKind.Reciprocal => arithmetic.Reciprocal(ref context, in source, destination),
            FloatVectorUnaryOpKind.Exp => arithmetic.Exp(ref context, in source, destination),
            FloatVectorUnaryOpKind.Log => arithmetic.Log(ref context, in source, destination),
            _ => throw new ArgumentOutOfRangeException(nameof(opKind))
        };
}
