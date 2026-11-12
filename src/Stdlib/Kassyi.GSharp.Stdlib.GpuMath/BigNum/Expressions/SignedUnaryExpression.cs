namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// A unary operation into a buffer of its own. The bound is given by the derived node when it is built.
internal abstract class SignedUnaryExpression(IGpuBigIntExpression target, long bits)
    : ISignedOperator, IBitBoundedExpression, IDisposable
{
    internal IGpuBigIntExpression Target { get; } = target ?? throw new ArgumentNullException(nameof(target));

    public long Bits { get; } = bits;

    public int NumWords => BigIntWidths.Words(Bits);

    public int OperandCount => 1;

    public bool PassesTargetToOperand => false;

    public IGpuBigIntExpression GetOperand(int index) => Target;

    public void Dispose() => SignedTree.Dispose(this);

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null) =>
        SignedTree.Evaluate(this, workspace, targetResult);

    public abstract SignedOperation Operation { get; }

    // As SignedBinaryExpression.Apply: the sign is planned, a host sign computes as before, a device sign is written by
    // a kernel
    public SignedValue Apply(GpuWorkspace workspace, SignedValue first, SignedValue second, GpuBigUInt? targetResult)
    {
        var plan = SignPlanner.For(Operation, first.Sign);
        GpuBigUInt staging = default;
        GpuBigUInt? rented = null;
        SignSource sign = default;

        try
        {
            var result = targetResult.HasValue
                ? ResultBuffers.Head(targetResult.Value, NumWords)
                : (rented = workspace.RentTemp(NumWords)).Value;

            // As in SignedBinaryExpression: a target that overlaps the operand gets the result through a temporary
            staging = SignedRoot.StagingFor(workspace, targetResult, NumWords, first.Magnitude, default);
            var buffer = staging.Handle.IsDefault ? result : staging;
            if (plan.IsHost)
            {
                Execute(SignedTree.WithHostSign(first), buffer);
                sign = SignSource.Host(plan.HostNegative);
            }
            else
            {
                sign = ExecuteDevice(workspace, first, buffer, plan);
            }

            var value = new SignedValue(SignedRoot.Deliver(staging, result, targetResult), sign);
            sign = default;
            rented = null;
            return value;
        }
        finally
        {
            try
            {
                sign.Dispose();
            }
            finally
            {
                SignedBinaryExpression.Release(first, default, staging, rented);
            }
        }
    }

    // A device sign plan: the magnitude with the operand taken as non-negative and the sign forwarded (Fit); ~
    // overrides it with its kernel
    protected virtual SignSource ExecuteDevice(GpuWorkspace workspace, SignedValue operand, GpuBigUInt resultBuffer,
        SignPlan plan)
    {
        Execute(new GpuBigInt(operand.Magnitude, false), resultBuffer);
        return plan.Kernel == SignKernel.Forward
            ? SignedDeviceOps.Xor(workspace, operand.Sign, SignSource.Host(false))
            : throw new InvalidOperationException($"{GetType().Name} has no device sign kernel {plan.Kernel}.");
    }

    // The operation on the evaluated operand into resultBuffer, which has exactly NumWords words
    protected abstract GpuBigInt Execute(GpuBigInt targetEval, GpuBigUInt resultBuffer);

    // The bound of the operand, rejecting a null operand before reading it
    protected static long OperandBits(IGpuBigIntExpression target) =>
        IBitBoundedExpression.Of(target ?? throw new ArgumentNullException(nameof(target)));
}
