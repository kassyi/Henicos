namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// The bound of every node is computed when the node is built, from the bounds its operands computed when they were
// built: reading it never walks the tree, however deep a loop made it.
internal abstract class SignedBinaryExpression(
    IGpuBigIntExpression left,
    IGpuBigIntExpression right,
    Func<long, long, long> bound
) : ISignedOperator, IBitBoundedExpression, IDisposable
{
    internal IGpuBigIntExpression Left { get; } = left ?? throw new ArgumentNullException(nameof(left));
    internal IGpuBigIntExpression Right { get; } = right ?? throw new ArgumentNullException(nameof(right));

    // After Left and Right, which reject a null operand first
    public long Bits { get; } = bound(IBitBoundedExpression.Of(left), IBitBoundedExpression.Of(right));

    public int NumWords => BigIntWidths.Words(Bits);

    public int OperandCount => 2;

    public bool PassesTargetToOperand => false;

    public IGpuBigIntExpression GetOperand(int index) => index == 0 ? Left : Right;

    public void Dispose() => SignedTree.Dispose(this);

    public GpuBigInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null) =>
        SignedTree.Evaluate(this, workspace, targetResult);

    public abstract SignedOperation Operation { get; }

    // The sign is planned first (SignPlanner): a host sign computes as before; a sign that depends on the values, or on
    // an operand's device sign, is written by a kernel into a device word, so nothing is read on the host here.
    public SignedValue Apply(GpuWorkspace workspace, SignedValue first, SignedValue second, GpuBigUInt? targetResult)
    {
        var plan = SignPlanner.For(Operation, first.Sign, second.Sign);
        GpuBigUInt staging = default;
        GpuBigUInt? rented = null;
        SignSource sign = default;

        try
        {
            var result = targetResult.HasValue
                ? ResultBuffers.Head(targetResult.Value, NumWords)
                : (rented = workspace.RentTemp(NumWords)).Value;

            // The kernels take __restrict__ pointers: a target that overlaps an operand gets the result through a
            // temporary
            staging = SignedRoot.StagingFor(workspace, targetResult, NumWords, first.Magnitude, second.Magnitude);
            var buffer = staging.Handle.IsDefault ? result : staging;
            if (plan.Chosen != ChosenOperand.None)
            {
                // Max / Min of different host signs: the plan names the operand, which is copied with its sign flag
                // and without a comparison
                SignedSelect.Copy(plan.Chosen == ChosenOperand.First ? first.Magnitude : second.Magnitude, buffer);
                sign = SignSource.Host(plan.HostNegative);
            }
            else if (plan.IsHost)
            {
                Execute(SignedTree.WithHostSign(first), SignedTree.WithHostSign(second), buffer);
                sign = SignSource.Host(plan.HostNegative);
            }
            else
            {
                sign = ExecuteDevice(workspace, first, second, buffer, plan);
            }

            var value = new SignedValue(SignedRoot.Deliver(staging, result, targetResult), sign);
            sign = default; // Handed to the value
            rented = null; // Evaluated successfully, do not return the result buffer
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
                Release(first, second, staging, rented);
            }
        }
    }

    // The magnitude with operands taken as non-negative (it does not depend on the signs, and no sign is read), and the
    // sign from the planned kernel. Add / Subtract, Max / Min and ~ override it with a kernel that does both.
    protected virtual SignSource ExecuteDevice(GpuWorkspace workspace, SignedValue a, SignedValue b,
        GpuBigUInt resultBuffer, SignPlan plan)
    {
        Execute(new GpuBigInt(a.Magnitude, false), new GpuBigInt(b.Magnitude, false), resultBuffer);
        return plan.Kernel switch
        {
            SignKernel.Xor => SignedDeviceOps.Xor(workspace, a.Sign, b.Sign),
            SignKernel.RemainderSign => SignedDeviceOps.RemainderSign(resultBuffer, a.Sign),
            _ => throw new InvalidOperationException($"{GetType().Name} has no device sign kernel {plan.Kernel}.")
        };
    }

    // A child result is owned (a rented intermediate) or borrowed (a leaf); Dispose returns only the former. Nested, so
    // that a release that throws (a failed capture refuses the clear) does not skip the others.
    internal static void Release(SignedValue leftResult, SignedValue rightResult, GpuBigUInt staging,
        GpuBigUInt? unusedResultBuffer)
    {
        try
        {
            leftResult.Dispose();
        }
        finally
        {
            try
            {
                rightResult.Dispose();
            }
            finally
            {
                try
                {
                    staging.Dispose();
                }
                finally
                {
                    unusedResultBuffer?.Dispose();
                }
            }
        }
    }

    // a op b into resultBuffer, which has exactly NumWords words
    protected abstract GpuBigInt Execute(GpuBigInt a, GpuBigInt b, GpuBigUInt resultBuffer);
}
