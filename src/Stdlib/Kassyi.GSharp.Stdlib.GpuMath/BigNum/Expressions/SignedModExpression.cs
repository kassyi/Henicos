namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class SignedModExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right, BigIntWidths.Remainder)
{
    public override SignedOperation Operation => SignedOperation.Remainder;

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        // The remainder is below |b| and at most |a|, so the low words of the divisor-wide remainder hold it
        var workspace = resultBuffer.Workspace;
        CaptureChecks.ThrowIfCapturing(workspace, CaptureChecks.DivisionReads);
        using var quotient = workspace.RentTemp(a.Value.NumWords);
        using var remainder = workspace.RentTemp(b.Value.NumWords);
        GpuBigIntDivRem.DivRem(a.Value, b.Value, quotient, remainder);
        resultBuffer.CopyFrom(remainder, numWords: resultBuffer.NumWords);

        // The magnitude only: the sign comes from the plan (+ for a non-negative dividend, RemainderSignKernel
        // otherwise), so nothing is read here
        return new GpuBigInt(resultBuffer, isNegative: false);
    }
}
