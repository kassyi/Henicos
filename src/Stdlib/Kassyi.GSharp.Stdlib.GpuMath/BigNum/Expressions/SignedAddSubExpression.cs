namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// a + b and a - b. Same effective host signs add the magnitudes into max + 1 bits, which never overflows; different
// signs (or a device sign) go to one kernel that subtracts, negates on a borrow and writes the sign into a device word.
// A zero difference is +0.
internal abstract class SignedAddSubExpression(IGpuBigIntExpression left, IGpuBigIntExpression right, bool subtract)
    : SignedBinaryExpression(left, right, BigIntWidths.Add)
{
    // Same effective host signs (the host plan): the magnitudes add, nothing is read
    protected override GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer)
    {
        BigIntWideOps.AddSubtract(a.Value, b.Value, resultBuffer, subtract: false);
        return new GpuBigInt(resultBuffer, a.IsNegative);
    }

    // Different signs, or a device sign: one kernel adds or subtracts by the effective signs and writes the sign
    protected override SignSource ExecuteDevice(GpuWorkspace workspace, SignedValue a, SignedValue b,
        GpuBigUInt resultBuffer, SignPlan plan) =>
        SignedDeviceOps.AddSubtract(a, b, subtract, resultBuffer);
}
