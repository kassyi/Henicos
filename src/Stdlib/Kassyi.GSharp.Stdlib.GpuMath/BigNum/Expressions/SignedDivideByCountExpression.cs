namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// x / count for a positive element count, truncated toward zero as C# integer division: the quotient of an Average.
// The count lives in a one-word temporary that the node rents and writes when it is evaluated (a memset, which a CUDA
// graph capture records) and returns at once, so the expression owns no buffer. The quotient is at most the dividend.
internal sealed class SignedDivideByCountExpression(IGpuBigIntExpression sum, uint count)
    : SignedUnaryExpression(sum, BigIntWidths.Divide(OperandBits(sum), BigIntWidths.BitsPerWord))
{
    internal uint Count { get; } = count > 0 ? count : throw new ArgumentOutOfRangeException(nameof(count));

    protected override GpuBigInt Execute(GpuBigInt targetEval, GpuBigUInt resultBuffer)
    {
        var workspace = resultBuffer.Workspace;
        using var divisor = workspace.RentTemp(1);
        using var remainder = workspace.RentTemp(1);
        workspace.Provider.WriteWord(divisor.DevicePointer, 0, Count, workspace.Stream);
        GpuBigIntDivRem.DivRem(targetEval.Value, divisor, resultBuffer, remainder);

        // The count is positive: the quotient has the sign of the sum (a zero quotient may keep it, as for /)
        return new GpuBigInt(resultBuffer, targetEval.IsNegative);
    }
}
