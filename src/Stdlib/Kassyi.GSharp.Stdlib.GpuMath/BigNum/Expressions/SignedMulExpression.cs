namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class SignedMulExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right, BigIntWidths.Multiply)
{
    public override SignedOperation Operation => SignedOperation.Multiply;

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        // The product kernels write the sum of the operand words; the bound may need fewer (a node narrower than its
        // words), and then the product goes through a temporary whose top words are zero
        var productWords = a.Value.NumWords + b.Value.NumWords;
        if (resultBuffer.NumWords == productWords)
        {
            GpuBigIntMath.Mul(a.Value, b.Value, resultBuffer);
        }
        else
        {
            using var product = resultBuffer.Workspace.RentTemp(productWords);
            GpuBigIntMath.Mul(a.Value, b.Value, product);
            resultBuffer.CopyFrom(product, numWords: resultBuffer.NumWords);
        }

        return new GpuBigInt(resultBuffer, a.IsNegative ^ b.IsNegative);
    }
}
