namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorCompareScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar,
    VectorCompareOperator opType) : IGpuBoolVectorExpression
{
    public int Length => vector.Length;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public GpuBigUIntArray Evaluate(GpuWorkspace workspace)
    {
        var vecResult = vector.Evaluate(workspace);
        var scalarResult = scalar.Evaluate(workspace);

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        var gridDimX = Length;
        var blockDimX = Math.Min(1024, vector.WordsPerElement);

        GpuBigIntCmpKernels.LaunchVectorCompareScalarKernelPtr(
            vecResult.Values.DevicePointer,
            scalarResult.Value.DevicePointer,
            _finalValues.DevicePointer,
            vector.WordsPerElement,
            (int)opType,
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

        vector.Dispose();
    }
}

internal class VectorCompareVectorExpression(
    [TakesOwnership] IGpuBigIntVectorExpression left,
    [TakesOwnership] IGpuBigIntVectorExpression right,
    VectorCompareOperator opType) : IGpuBoolVectorExpression
{
    public int Length => left.Length;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public GpuBigUIntArray Evaluate(GpuWorkspace workspace)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("Lengths must match");
        }

        var words = Math.Max(left.WordsPerElement, right.WordsPerElement);

        var leftResult = left.Evaluate(workspace);
        var rightResult = right.Evaluate(workspace);

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        var gridDimX = Length;
        var blockDimX = Math.Min(1024, words);

        GpuBigIntCmpKernels.LaunchVectorCompareVectorKernelPtr(
            leftResult.Values.DevicePointer,
            rightResult.Values.DevicePointer,
            _finalValues.DevicePointer,
            words,
            (int)opType,
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

        left.Dispose();
        right.Dispose();
    }
}
