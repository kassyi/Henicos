namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class SignedDivExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right, BigIntWidths.Divide)
{
    public override SignedOperation Operation => SignedOperation.Divide;

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        CaptureChecks.ThrowIfCapturing(resultBuffer.Workspace, CaptureChecks.DivisionReads);
        using var remainder = resultBuffer.Workspace.RentTemp(b.Value.NumWords);
        GpuBigIntDivRem.DivRem(a.Value, b.Value, resultBuffer, remainder);
        return new GpuBigInt(resultBuffer, a.IsNegative ^ b.IsNegative);
    }
}
