namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class SignedMinExpression(IGpuBigIntExpression left, IGpuBigIntExpression right)
    : SignedBinaryExpression(left, right, Math.Max)
{
    public override SignedOperation Operation => SignedOperation.Min;

    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer) =>
        throw SignedSelect.NotOnTheHost();

    protected override SignSource ExecuteDevice(GpuWorkspace workspace, SignedValue a, SignedValue b,
        GpuBigUInt resultBuffer, SignPlan plan) =>
        SignedDeviceOps.Select(a, b, max: false, resultBuffer, plan);
}
