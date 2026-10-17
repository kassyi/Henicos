namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorCompareScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar,
    VectorCompareOperator opType) : IGpuBoolVectorExpression
{
    public int Length => vector.Length;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public GpuBigUIntArray Evaluate(GpuWorkspace workspace)
    {
        var vecResult = vector.Evaluate(workspace);

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        if (Length == 0)
        {
            return _finalValues;
        }

        var scalarSig = scalar.Significand;
        var maxWords = Math.Max(vecResult.WordsPerElement, scalarSig.NumWords);
        var blockDimX = (uint)Math.Clamp(maxWords, 32, 1024);

        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatCompare
        {
            A = vecResult.Significands.DevicePointer,
            B = scalarSig.DevicePointer,
            Result = _finalValues.DevicePointer,
            ExpA = vecResult.Exponents.DevicePointer,
            ExpB = vecResult.Exponents.DevicePointer,
            SignA = vecResult.Signs.DevicePointer,
            SignB = vecResult.Signs.DevicePointer,
            NumWordsA = vecResult.WordsPerElement,
            NumWordsB = scalarSig.NumWords,
            ScalarExp = scalar.Exponent,
            ScalarSign = scalar.IsNegative ? 1u : 0u,
            IsScalar = true,
            OpType = (int)opType
        };

        GpuBigIntVectorKernels.LaunchVectorFloatCompareKernelPtr(
            ctx, (uint)Length, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);

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


internal sealed class FloatVectorCompareVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right,
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

        var leftResult = left.Evaluate(workspace);
        var rightResult = right.Evaluate(workspace);

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, Length, 1);
            _allocated = true;
        }

        if (Length == 0)
        {
            return _finalValues;
        }

        var maxWords = Math.Max(leftResult.WordsPerElement, rightResult.WordsPerElement);
        var blockDimX = (uint)Math.Clamp(maxWords, 32, 1024);

        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatCompare
        {
            A = leftResult.Significands.DevicePointer,
            B = rightResult.Significands.DevicePointer,
            Result = _finalValues.DevicePointer,
            ExpA = leftResult.Exponents.DevicePointer,
            ExpB = rightResult.Exponents.DevicePointer,
            SignA = leftResult.Signs.DevicePointer,
            SignB = rightResult.Signs.DevicePointer,
            NumWordsA = leftResult.WordsPerElement,
            NumWordsB = rightResult.WordsPerElement,
            ScalarExp = 0,
            ScalarSign = 0u,
            IsScalar = false,
            OpType = (int)opType
        };

        GpuBigIntVectorKernels.LaunchVectorFloatCompareKernelPtr(
            ctx, (uint)Length, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);

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
