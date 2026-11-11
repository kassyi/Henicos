namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class FloatVectorCompareScalarExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar,
    VectorCompareOperator opType) : IGpuBoolVectorExpression, IBoolVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(vector);

    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(vector);

    public bool MayThrow => _inputsMayThrow;

    // The flags, allocated at the first evaluation
    private readonly VectorFlags _flags = new();

    public int Length => _shape.Length;
    public int Capacity => _shape.Capacity;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 1;

    public object GetOperand(int index) => vector;

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second) =>
        VectorResult.Of(Apply(workspace, first.Float));

    private BoolVectorEvaluation Apply(GpuWorkspace workspace, FloatVectorEvaluation evaluated)
    {
        var flags = _flags.Get(workspace, Capacity);
        if (Capacity == 0)
        {
            return new BoolVectorEvaluation(flags, evaluated.Count);
        }

        var vecResult = evaluated.Storage;
        var scalarSig = scalar.Significand;
        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorFloatCompare
        {
            Left = FloatVectorCompareLaunch.Operand(vecResult),
            Right = new GpuBigIntVectorKernels.AotContexts.FloatVectorOperandReadOnly
            {
                Limbs = scalarSig.ToProxy(),
                Exponents = vecResult.Exponents.ToProxy(),
                Signs = vecResult.Signs.ToProxy(),
                NumWords = scalarSig.NumWords
            },
            Result = flags.ToProxy(),
            ScalarExp = scalar.Exponent,
            ScalarSign = scalar.IsNegative ? 1u : 0u,
            IsScalar = true,
            OpType = (int)opType
        };

        FloatVectorCompareLaunch.Launch(workspace, ctx, Capacity, vecResult.WordsPerElement, scalarSig.NumWords);
        return new BoolVectorEvaluation(flags, evaluated.Count);
    }

    // One level of a recursive evaluation (VectorWalk.EvaluateDirect)
    public VectorResult EvaluateDirect(GpuWorkspace workspace, scoped in VectorRequest request,
        RecursionLevel level)
    {
        return Apply(workspace, request, VectorWalk.EvaluateDirect(vector, workspace, VectorRequest.Whole, level),
            default);
    }

    public void DisposeOwn() => _flags.Dispose();

    public void Dispose() => VectorWalk.Dispose(this);
}
