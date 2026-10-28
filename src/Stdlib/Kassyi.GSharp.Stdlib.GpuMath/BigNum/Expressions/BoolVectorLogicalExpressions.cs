namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorLogicalExpression(
    [TakesOwnership] IGpuBoolVectorExpression left,
    [TakesOwnership] IGpuBoolVectorExpression right,
    bool isAnd) : IGpuBoolVectorExpression, IDeviceCountedBoolVector
{
    public int Length => left.Length;
    public int Capacity => left.Capacity;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public bool TryGetLength(out int length) => left.TryGetLength(out length);

    public GpuBigUIntArrayView Evaluate(GpuWorkspace workspace) =>
        DeviceCounted.Materialize(workspace, EvaluateCore(workspace));

    // The flags of the capacity; both inputs have the same count, or one of them a device count (design J4)
    public BoolVectorEvaluation EvaluateCore(GpuWorkspace workspace)
    {
        if (left.Capacity != right.Capacity)
        {
            throw new ArgumentException("Lengths must match");
        }

        var leftResult = DeviceCounted.Evaluate(left, workspace);
        var rightResult = DeviceCounted.Evaluate(right, workspace);
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
                leftResult.Flags.DevicePointer,
                rightResult.Flags.DevicePointer,
                _finalValues.DevicePointer,
                capacity,
                (uint)gridDimX, 1, 1,
                (uint)blockDimX, 1, 1,
                0, workspace.Stream);
        }
        else
        {
            GpuBigIntCmpKernels.LaunchVectorOrKernelPtr(
                leftResult.Flags.DevicePointer,
                rightResult.Flags.DevicePointer,
                _finalValues.DevicePointer,
                capacity,
                (uint)gridDimX, 1, 1,
                (uint)blockDimX, 1, 1,
                0, workspace.Stream);
        }

        return new BoolVectorEvaluation(_finalValues, count);
    }

    public void Dispose()
    {
        if (_allocated)
        {
            _finalValues.Dispose();
            _allocated = false;
        }

        left.Dispose();
        right.Dispose();
    }
}

internal class VectorNotExpression([TakesOwnership] IGpuBoolVectorExpression source)
    : IGpuBoolVectorExpression, IDeviceCountedBoolVector
{
    public int Length => source.Length;
    public int Capacity => source.Capacity;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public bool TryGetLength(out int length) => source.TryGetLength(out length);

    public GpuBigUIntArrayView Evaluate(GpuWorkspace workspace) =>
        DeviceCounted.Materialize(workspace, EvaluateCore(workspace));

    public BoolVectorEvaluation EvaluateCore(GpuWorkspace workspace)
    {
        var result = DeviceCounted.Evaluate(source, workspace);

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
            result.Flags.DevicePointer,
            _finalValues.DevicePointer,
            capacity,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

        return new BoolVectorEvaluation(_finalValues, result.Count);
    }

    public void Dispose()
    {
        if (_allocated)
        {
            _finalValues.Dispose();
            _allocated = false;
        }

        source.Dispose();
    }
}

/// <summary>
/// Any / All over the first Count flags of a vector: the kernels read the count on the device (design D3 4.3), so the
/// flags after it do not take part, and an empty vector gives false / true, as System.Linq.
/// </summary>
internal static class BoolVectorQuantifier
{
    public static bool Evaluate(GpuWorkspace workspace, IGpuBoolVectorExpression source, bool all)
    {
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
            Flags = flags.Flags.DevicePointer,
            Result = result.DevicePointer,
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
