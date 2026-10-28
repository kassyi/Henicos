namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorLogicalExpression(
    [TakesOwnership] IGpuBoolVectorExpression left,
    [TakesOwnership] IGpuBoolVectorExpression right,
    bool isAnd) : IGpuBoolVectorExpression
{
    public int Length => left.Length;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public GpuBigUIntArrayView Evaluate(GpuWorkspace workspace)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("Lengths must match");
        }

        var leftResult = left.Evaluate(workspace);
        var rightResult = right.Evaluate(workspace);

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        // And / Or of empty vectors launches nothing: a grid of 0 blocks is an invalid launch.
        if (Length == 0)
        {
            return _finalValues;
        }

        var gridDimX = (Length + 255) / 256;
        var blockDimX = 256;

        if (isAnd)
        {
            GpuBigIntCmpKernels.LaunchVectorAndKernelPtr(
                leftResult.DevicePointer,
                rightResult.DevicePointer,
                _finalValues.DevicePointer,
                Length,
                (uint)gridDimX, 1, 1,
                (uint)blockDimX, 1, 1,
                0, workspace.Stream);
        }
        else
        {
            GpuBigIntCmpKernels.LaunchVectorOrKernelPtr(
                leftResult.DevicePointer,
                rightResult.DevicePointer,
                _finalValues.DevicePointer,
                Length,
                (uint)gridDimX, 1, 1,
                (uint)blockDimX, 1, 1,
                0, workspace.Stream);
        }

        return _finalValues;
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

internal class VectorNotExpression([TakesOwnership] IGpuBoolVectorExpression source) : IGpuBoolVectorExpression
{
    public int Length => source.Length;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public GpuBigUIntArrayView Evaluate(GpuWorkspace workspace)
    {
        var result = source.Evaluate(workspace);

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        // Not of empty vectors launches nothing: a grid of 0 blocks is an invalid launch.
        if (Length == 0)
        {
            return _finalValues;
        }

        var gridDimX = (Length + 255) / 256;
        var blockDimX = 256;

        GpuBigIntCmpKernels.LaunchVectorNotKernelPtr(
            result.DevicePointer,
            _finalValues.DevicePointer,
            Length,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

        return _finalValues;
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

internal class VectorAnyTrueExpression([TakesOwnership] IGpuBoolVectorExpression source) : IGpuBoolExpr
{
    public bool Evaluate(GpuWorkspace workspace)
    {
        var result = source.Evaluate(workspace);

        // Any of nothing is false, as in System.Linq; nothing is launched.
        if (source.Length == 0)
        {
            return false;
        }

        using var tempResult = new GpuBigUIntArray(workspace, 1, 1);
        workspace.Provider.ClearMemoryAsync(tempResult.DevicePointer, 1, workspace.Stream);

        var gridDimX = (source.Length + 255) / 256;
        var blockDimX = 256;

        GpuBigIntCmpKernels.LaunchVectorAnyTrueKernelPtr(
            result.DevicePointer,
            tempResult.DevicePointer,
            source.Length,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

        return workspace.Provider.ReadWord(tempResult.DevicePointer, 0, workspace.Stream) != 0;
    }

    public void Dispose() => source.Dispose();
}

internal class VectorAllTrueExpression([TakesOwnership] IGpuBoolVectorExpression source) : IGpuBoolExpr
{
    public bool Evaluate(GpuWorkspace workspace)
    {
        var result = source.Evaluate(workspace);

        // All of nothing is true, as in System.Linq; nothing is launched.
        if (source.Length == 0)
        {
            return true;
        }

        using var tempResult = new GpuBigUIntArray(workspace, 1, 1);

        // Initialize with 1
        workspace.Provider.WriteWord(tempResult.DevicePointer, 0, 1u, workspace.Stream);

        var gridDimX = (source.Length + 255) / 256;
        var blockDimX = 256;

        GpuBigIntCmpKernels.LaunchVectorAllTrueKernelPtr(
            result.DevicePointer,
            tempResult.DevicePointer,
            source.Length,
            (uint)gridDimX, 1, 1,
            (uint)blockDimX, 1, 1,
            0, workspace.Stream);

        return workspace.Provider.ReadWord(tempResult.DevicePointer, 0, workspace.Stream) != 0;
    }

    public void Dispose() => source.Dispose();
}
