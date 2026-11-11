namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorCompareScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar,
    VectorCompareOperator opType) : IGpuBoolVectorExpression, IBoolVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(vector);

    public int Length => _shape.Length;
    public int Capacity => _shape.Capacity;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 1;

    public object GetOperand(int index) => vector;

    public VectorValue Apply(GpuWorkspace workspace, in VectorRequest request, in VectorValue first,
        in VectorValue second) =>
        VectorValue.Of(Apply(workspace, first.Float));

    private BoolVectorEvaluation Apply(GpuWorkspace workspace, FloatVectorEvaluation evaluated)
    {
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
                Limbs = vecResult.Significands.ToProxy(),
                Exponents = vecResult.Exponents.ToProxy(),
                Signs = vecResult.Signs.ToProxy(),
                NumWords = vecResult.WordsPerElement
            },
            Right = new GpuBigIntVectorKernels.AotContexts.FloatVectorOperandReadOnly
            {
                Limbs = scalarSig.ToProxy(),
                Exponents = vecResult.Exponents.ToProxy(),
                Signs = vecResult.Signs.ToProxy(),
                NumWords = scalarSig.NumWords
            },
            Result = _finalValues.ToProxy(),
            ScalarExp = scalar.Exponent,
            ScalarSign = scalar.IsNegative ? 1u : 0u,
            IsScalar = true,
            OpType = (int)opType
        };

        GpuBigIntVectorKernels.LaunchVectorFloatCompareKernelPtr(
            ctx, (uint)capacity, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);

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

internal sealed class FloatVectorCompareVectorExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression left,
    [TakesOwnership] IGpuBigFloatVectorExpression right,
    VectorCompareOperator opType) : IGpuBoolVectorExpression, IBoolVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(left);

    public int Length => _shape.Length;
    public int Capacity => _shape.Capacity;

    // The capacity of the right input, for the length check before anything is evaluated
    private readonly int _rightCapacity = right.Capacity;
    private GpuBigUIntArray _finalValues;
    private bool _allocated;

    // Evaluates the operands so that a shared sub-expression read at two offsets keeps both results
    private readonly FloatVectorOperandPair _operands = new();

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
        in VectorValue second)
    {
        var leftEvaluated = first.Float;
        var rightEvaluated = second.Float;
        _operands.KeepBoth(left, workspace, 0, null, ref leftEvaluated, ref rightEvaluated);
        return VectorValue.Of(Apply(workspace, leftEvaluated, rightEvaluated));
    }

    private BoolVectorEvaluation Apply(GpuWorkspace workspace, FloatVectorEvaluation leftEvaluated,
        FloatVectorEvaluation rightEvaluated)
    {
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
                Limbs = leftResult.Significands.ToProxy(),
                Exponents = leftResult.Exponents.ToProxy(),
                Signs = leftResult.Signs.ToProxy(),
                NumWords = leftResult.WordsPerElement
            },
            Right = new GpuBigIntVectorKernels.AotContexts.FloatVectorOperandReadOnly
            {
                Limbs = rightResult.Significands.ToProxy(),
                Exponents = rightResult.Exponents.ToProxy(),
                Signs = rightResult.Signs.ToProxy(),
                NumWords = rightResult.WordsPerElement
            },
            Result = _finalValues.ToProxy(),
            ScalarExp = 0,
            ScalarSign = 0u,
            IsScalar = false,
            OpType = (int)opType
        };

        GpuBigIntVectorKernels.LaunchVectorFloatCompareKernelPtr(
            ctx, (uint)capacity, 1, 1, blockDimX, 1, 1, 0, workspace.Stream);

        return new BoolVectorEvaluation(_finalValues, count);
    }

    public void DisposeOwn()
    {
        if (_allocated)
        {
            _finalValues.Dispose();
            _allocated = false;
        }

        _operands.Dispose();
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
