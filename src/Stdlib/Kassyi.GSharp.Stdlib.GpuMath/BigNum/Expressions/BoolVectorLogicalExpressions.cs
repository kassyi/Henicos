namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorLogicalExpression(
    [TakesOwnership] IGpuBoolVectorExpression left,
    [TakesOwnership] IGpuBoolVectorExpression right,
    bool isAnd) : IGpuBoolVectorExpression, IBoolVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(left);

    public int Length => _shape.Length;
    public int Capacity => _shape.Capacity;

    // The capacity of the right input, for the length check before anything is evaluated
    private readonly int _rightCapacity = right.Capacity;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 2;

    public object GetOperand(int index) => index == 0 ? left : right;

    // The lengths are checked before the inputs are evaluated, so that a rejected request evaluates nothing
    public void Enter(in VectorRequest request)
    {
        if (Capacity != _rightCapacity)
        {
            throw new ArgumentException("Lengths must match");
        }
    }

    public VectorValue Apply(GpuWorkspace workspace, in VectorRequest request, in VectorValue first,
        in VectorValue second) =>
        VectorValue.Of(Apply(workspace, first.Bool, second.Bool));

    // The flags of the capacity; both inputs have the same count, or one of them a device count
    private BoolVectorEvaluation Apply(GpuWorkspace workspace, BoolVectorEvaluation leftResult,
        BoolVectorEvaluation rightResult)
    {
        var count = DeviceCounted.Combine(leftResult.Count, rightResult.Count);

        var capacity = Capacity;
        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, capacity, 1);
            _allocated = true;
        }

        // And / Or of empty vectors launches nothing: a grid of 0 blocks is an invalid launch.
        if (capacity == 0)
        {
            return new BoolVectorEvaluation(_finalValues, count);
        }

        var gridDimX = (capacity + 255) / 256;
        var blockDimX = 256;

        if (isAnd)
        {
            GpuBigIntCmpKernels.LaunchVectorAndKernelPtr(
                leftResult.Flags.ToProxy(),
                rightResult.Flags.ToProxy(),
                _finalValues.ToProxy(),
                capacity,
                (uint)gridDimX, 1, 1,
                (uint)blockDimX, 1, 1,
                0, workspace.Stream);
        }
        else
        {
            GpuBigIntCmpKernels.LaunchVectorOrKernelPtr(
                leftResult.Flags.ToProxy(),
                rightResult.Flags.ToProxy(),
                _finalValues.ToProxy(),
                capacity,
                (uint)gridDimX, 1, 1,
                (uint)blockDimX, 1, 1,
                0, workspace.Stream);
        }

        return new BoolVectorEvaluation(_finalValues, count);
    }

    public void DisposeOwn()
    {
        if (_allocated)
        {
            _finalValues.Dispose();
            _allocated = false;
        }
    }

    public void Dispose()
    {
        try
        {
            DisposeOwn();
        }
        finally
        {
            VectorWalk.DisposeInputs(this);
        }
    }
}

internal class VectorNotExpression([TakesOwnership] IGpuBoolVectorExpression source)
    : IGpuBoolVectorExpression, IBoolVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(source);

    public int Length => _shape.Length;
    public int Capacity => _shape.Capacity;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 1;

    public object GetOperand(int index) => source;

    public VectorValue Apply(GpuWorkspace workspace, in VectorRequest request, in VectorValue first,
        in VectorValue second) =>
        VectorValue.Of(Apply(workspace, first.Bool));

    private BoolVectorEvaluation Apply(GpuWorkspace workspace, BoolVectorEvaluation result)
    {
        var capacity = Capacity;
        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, capacity, 1);
            _allocated = true;
        }

        // Not of empty vectors launches nothing: a grid of 0 blocks is an invalid launch.
        if (capacity == 0)
        {
            return new BoolVectorEvaluation(_finalValues, result.Count);
        }

        var gridDimX = (capacity + 255) / 256;
        var blockDimX = 256;

        GpuBigIntCmpKernels.LaunchVectorNotKernelPtr(
            result.Flags.ToProxy(),
            _finalValues.ToProxy(),
            capacity,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

        return new BoolVectorEvaluation(_finalValues, result.Count);
    }

    public void DisposeOwn()
    {
        if (_allocated)
        {
            _finalValues.Dispose();
            _allocated = false;
        }
    }

    public void Dispose()
    {
        try
        {
            DisposeOwn();
        }
        finally
        {
            VectorWalk.DisposeInputs(this);
        }
    }
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
        // A vector with no capacity has no element: nothing is evaluated or launched
        if (source.Capacity == 0)
        {
            return all;
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
