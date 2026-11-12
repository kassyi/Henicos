namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// ~x == -x - 1, as BigInteger and C# int: -(|x| + 1) for a non-negative x, |x| - 1 for a negative one. The result is
// one bit wider than x (GMP mpz_com). For a negative x the kernel tells -0 (whose ~ is -1) apart on the device.
internal class SignedBitwiseNotExpression(IGpuBigIntExpression target)
    : SignedUnaryExpression(target, BigIntWidths.SignedNot(OperandBits(target)))
{
    public override SignedOperation Operation => SignedOperation.Not;

    // A non-negative host x (the host plan): -(|x| + 1); the result is wider than the bound of x, so the carry is kept
    protected override GpuBigInt Execute(GpuBigInt targetEval, GpuBigUInt resultBuffer)
    {
        GpuBigIntMath.Add(targetEval.Value, 1u, resultBuffer);
        return new GpuBigInt(resultBuffer, isNegative: true);
    }

    // A negative or device-signed x: one kernel computes |x| - 1 (or -1 for -0) or |x| + 1 and writes the sign
    protected override SignSource ExecuteDevice(GpuWorkspace workspace, SignedValue operand, GpuBigUInt resultBuffer,
        SignPlan plan) =>
        SignedDeviceOps.Not(operand, resultBuffer);
}
