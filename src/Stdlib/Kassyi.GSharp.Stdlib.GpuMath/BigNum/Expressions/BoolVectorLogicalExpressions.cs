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

internal class VectorNotExpression([TakesOwnership] IGpuBoolVectorExpression source)
    : IGpuBoolVectorExpression, IBoolVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(source);

    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(source);

    public bool MayThrow => _inputsMayThrow;

    // The flags, allocated at the first evaluation
    private readonly VectorFlags _flags = new();

    public int Length => _shape.Length;
    public int Capacity => _shape.Capacity;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 1;

    public object GetOperand(int index) => source;

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second) =>
        VectorResult.Of(Apply(workspace, first.Bool));

    private BoolVectorEvaluation Apply(GpuWorkspace workspace, BoolVectorEvaluation result)
    {
        var flags = _flags.Get(workspace, Capacity);

        // Not of empty vectors launches nothing: a grid of 0 blocks is an invalid launch.
        if (Capacity > 0)
        {
            GpuBigIntCmpKernels.LaunchVectorNotKernelPtr(result.Flags.ToProxy(), flags.ToProxy(), Capacity,
                (uint)((Capacity + 255) / 256), 1, 1, 256, 1, 1, 0, workspace.Stream);
        }

        return new BoolVectorEvaluation(flags, result.Count);
    }

    // One level of a recursive evaluation (VectorWalk.EvaluateDirect)
    public VectorResult EvaluateDirect(GpuWorkspace workspace, scoped in VectorRequest request,
        RecursionLevel level)
    {
        return Apply(workspace, request, VectorWalk.EvaluateDirect(source, workspace, VectorRequest.Whole, level),
            default);
    }

    public void DisposeOwn() => _flags.Dispose();

    public void Dispose() => VectorWalk.Dispose(this);
}

/// <summary>
/// Any / All over the first Count flags of a vector: the kernels read the count on the device, so the
/// flags after it do not take part, and an empty vector gives false / true, as System.Linq.
/// </summary>
internal static class BoolVectorQuantifier
{
    public static bool Evaluate(GpuWorkspace workspace, IGpuBoolVectorExpression source, bool all)
    {
        DeviceCounted.ThrowIfCpuFallback(workspace);
        if (source.Capacity == 0)
        {
            return EvaluateEmpty(workspace, source, all);
        }

        var flags = DeviceCounted.Evaluate(source, workspace);
        using var result = new GpuBigUIntArray(workspace, 1, 1);
        workspace.Provider.WriteWord(result.DevicePointer, 0, all ? 1u : 0u, workspace.Stream);

        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorBoolReduction
        {
            Flags = flags.Flags.ToProxy(),
            Result = result.ToProxy(),
            Count = flags.Count.ToContext()
        };

        var gridDimX = (uint)((flags.Count.Capacity + 255) / 256);
        if (all)
        {
            GpuBigIntVectorKernels.LaunchVectorAllTrueKernelPtr(ctx, gridDimX, 1, 1, 256, 1, 1, 0, workspace.Stream);
        }
        else
        {
            GpuBigIntVectorKernels.LaunchVectorAnyTrueKernelPtr(ctx, gridDimX, 1, 1, 256, 1, 1, 0, workspace.Stream);
        }

        return workspace.Provider.ReadWord(result.DevicePointer, 0, workspace.Stream) != 0;
    }

    // A vector with no capacity has no element: nothing is launched, and nothing is evaluated unless the evaluation
    // can throw (inputs of different lengths)
    private static bool EvaluateEmpty(GpuWorkspace workspace, IGpuBoolVectorExpression source, bool all)
    {
        DeviceCounted.EvaluateIfMayThrow(source, workspace);
        return all;
    }
}

internal class VectorAnyTrueExpression([TakesOwnership] IGpuBoolVectorExpression source) : IGpuBoolExpr
{
    public bool Evaluate(GpuWorkspace workspace) => BoolVectorQuantifier.Evaluate(workspace, source, all: false);

    public void Dispose() => source.Dispose();
}

internal class VectorAllTrueExpression([TakesOwnership] IGpuBoolVectorExpression source) : IGpuBoolExpr
{
    public bool Evaluate(GpuWorkspace workspace) => BoolVectorQuantifier.Evaluate(workspace, source, all: true);

    public void Dispose() => source.Dispose();
}
