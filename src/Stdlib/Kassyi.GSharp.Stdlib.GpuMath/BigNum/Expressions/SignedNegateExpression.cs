namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// -x: the operand evaluated into the node's own target, with the sign flipped
internal class SignedNegateExpression(IGpuBigIntExpression target) : ISignedOperator, IBitBoundedExpression, IDisposable
{
    internal IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));

    public long Bits { get; } = IBitBoundedExpression.Of(target);

    public int NumWords => BigIntWidths.Words(Bits);

    public int OperandCount => 1;

    public bool PassesTargetToOperand => true;

    public IGpuBigIntExpression GetOperand(int index) => Target;

    public void Dispose() => SignedTree.Dispose(this);

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null) =>
        SignedTree.Evaluate(this, workspace, targetResult);

    public SignedOperation Operation => SignedOperation.Negate;

    // The magnitude of the operand with the sign flipped: a host sign on the host, a device sign by a one-thread XOR
    public SignedValue Apply(GpuWorkspace workspace, SignedValue first, SignedValue second, GpuBigUInt? targetResult)
    {
        if (!first.Sign.IsDevice)
        {
            return new SignedValue(first.Magnitude, SignSource.Host(!first.Sign.HostNegative));
        }

        try
        {
            return new SignedValue(first.Magnitude,
                SignedDeviceOps.Xor(workspace, first.Sign, SignSource.Host(true)));
        }
        finally
        {
            first.Sign.Dispose();
        }
    }
}
