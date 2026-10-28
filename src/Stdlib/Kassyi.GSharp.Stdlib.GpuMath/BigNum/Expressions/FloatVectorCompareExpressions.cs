namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorCompareScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar,
    VectorCompareOperator opType) : IGpuBoolVectorExpression
{
    public int Length => vector.Length;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public GpuBigUIntArrayView Evaluate(GpuWorkspace workspace)
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
            Left = new GpuBigIntVectorKernels.AotContexts.FloatVectorOperandReadOnly
            {
                Limbs = vecResult.Significands.DevicePointer,
                Exponents = vecResult.Exponents.DevicePointer,
                Signs = vecResult.Signs.DevicePointer,
                NumWords = vecResult.WordsPerElement
            },
            Right = new GpuBigIntVectorKernels.AotContexts.FloatVectorOperandReadOnly
            {
                Limbs = scalarSig.DevicePointer,
                Exponents = vecResult.Exponents.DevicePointer,
                Signs = vecResult.Signs.DevicePointer,
                NumWords = scalarSig.NumWords
            },
            Result = _finalValues.DevicePointer,
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

        if (Length == 0)
        {
            return _finalValues;
        }

        var maxWords = Math.Max(leftResult.WordsPerElement, rightResult.WordsPerElement);
        var blockDimX = (uint)Math.Clamp(maxWords, 32, 1024);

        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatCompare
        {
            Left = new GpuBigIntVectorKernels.AotContexts.FloatVectorOperandReadOnly
            {
                Limbs = leftResult.Significands.DevicePointer,
                Exponents = leftResult.Exponents.DevicePointer,
                Signs = leftResult.Signs.DevicePointer,
                NumWords = leftResult.WordsPerElement
            },
            Right = new GpuBigIntVectorKernels.AotContexts.FloatVectorOperandReadOnly
            {
                Limbs = rightResult.Significands.DevicePointer,
                Exponents = rightResult.Exponents.DevicePointer,
                Signs = rightResult.Signs.DevicePointer,
                NumWords = rightResult.WordsPerElement
            },
            Result = _finalValues.DevicePointer,
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
