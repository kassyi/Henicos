namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorLogicalExpression(
    [TakesOwnership] IGpuBoolVectorExpression left,
    [TakesOwnership] IGpuBoolVectorExpression right,
    bool isAnd) : IGpuBoolVectorExpression, IBoolVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(left);

    // The capacity of the right input, for the length check before anything is evaluated
    private readonly int _rightCapacity = right.Capacity;

    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(left) || VectorEffects.MayThrow(right);

    // Inputs of different lengths are rejected
    public bool MayThrow => _inputsMayThrow || Capacity != _rightCapacity;

    // The flags, allocated at the first evaluation
    private readonly VectorFlags _flags = new();

    public int Length => _shape.Length;
    public int Capacity => _shape.Capacity;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 2;

    public object GetOperand(int index) => index == 0 ? left : right;

    // The lengths are checked before the inputs are evaluated, so that a rejected request evaluates nothing
    public void Enter(in VectorRequest request) =>
        VectorShape.ThrowIfLengthsDiffer(Capacity, _rightCapacity, "Lengths must match");

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second) =>
        VectorResult.Of(Apply(workspace, first.Bool, second.Bool));

    // The flags of the capacity; both inputs have the same count, or one of them a device count
    private BoolVectorEvaluation Apply(GpuWorkspace workspace, BoolVectorEvaluation leftResult,
        BoolVectorEvaluation rightResult)
    {
        var count = DeviceCounted.Combine(leftResult.Count, rightResult.Count);
        var flags = _flags.Get(workspace, Capacity);

        // And / Or of empty vectors launches nothing: a grid of 0 blocks is an invalid launch.
        if (Capacity > 0)
        {
            Launch(workspace, leftResult.Flags, rightResult.Flags, flags);
        }

        return new BoolVectorEvaluation(flags, count);
    }

    private void Launch(GpuWorkspace workspace, GpuBigUIntArrayView leftFlags, GpuBigUIntArrayView rightFlags,
        GpuBigUIntArray flags)
    {
        var gridDimX = (uint)((Capacity + 255) / 256);
        if (isAnd)
        {
            GpuBigIntCmpKernels.LaunchVectorAndKernelPtr(leftFlags.ToProxy(), rightFlags.ToProxy(), flags.ToProxy(),
                Capacity, gridDimX, 1, 1, 256, 1, 1, 0, workspace.Stream);
        }
        else
        {
            GpuBigIntCmpKernels.LaunchVectorOrKernelPtr(leftFlags.ToProxy(), rightFlags.ToProxy(), flags.ToProxy(),
                Capacity, gridDimX, 1, 1, 256, 1, 1, 0, workspace.Stream);
        }
    }

    // One level of a recursive evaluation (VectorWalk.EvaluateDirect)
    public VectorResult EvaluateDirect(GpuWorkspace workspace, scoped in VectorRequest request,
        RecursionLevel level)
    {
        Enter(request);

        var first = VectorWalk.EvaluateDirect(left, workspace, VectorRequest.Whole, level);
        var second = VectorWalk.EvaluateDirect(right, workspace, VectorRequest.Whole, level);
        return Apply(workspace, request, first, second);
    }

    public void DisposeOwn() => _flags.Dispose();

    public void Dispose() => VectorWalk.Dispose(this);
}
