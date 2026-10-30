namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorCompareScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar,
    VectorCompareOperator opType) : IGpuBoolVectorExpression, IDeviceCountedBoolVector
{
    public int Length => vector.Length;
    public int Capacity => vector.Capacity;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public bool TryGetLength(out int length) => vector.TryGetLength(out length);

    public BoolVectorEvaluation EvaluateCore(GpuWorkspace workspace)
    {
        var evaluated = DeviceCounted.Evaluate(vector, workspace);
        var vecResult = evaluated.Storage;
        var count = evaluated.Count;
        var capacity = Capacity;

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, capacity, 1);
            _allocated = true;
        }

        if (capacity == 0)
        {
            return new BoolVectorEvaluation(_finalValues, count);
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
            ctx, (uint)capacity, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);

        return new BoolVectorEvaluation(_finalValues, count);
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
    VectorCompareOperator opType) : IGpuBoolVectorExpression, IDeviceCountedBoolVector
{
    public int Length => left.Length;
    public int Capacity => left.Capacity;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public bool TryGetLength(out int length) => left.TryGetLength(out length);

    public BoolVectorEvaluation EvaluateCore(GpuWorkspace workspace)
    {
        if (left.Capacity != right.Capacity)
        {
            throw new ArgumentException("Lengths must match");
        }

        var leftEvaluated = DeviceCounted.Evaluate(left, workspace);
        var rightEvaluated = DeviceCounted.Evaluate(right, workspace);
        var count = DeviceCounted.Combine(leftEvaluated.Count, rightEvaluated.Count);
        var leftResult = leftEvaluated.Storage;
        var rightResult = rightEvaluated.Storage;
        var capacity = Capacity;

        if (!_allocated)
        {
            _finalValues = new GpuBigUIntArray(workspace, capacity, 1);
            _allocated = true;
        }

        if (capacity == 0)
        {
            return new BoolVectorEvaluation(_finalValues, count);
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
            ctx, (uint)capacity, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);

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
