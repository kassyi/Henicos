namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class VectorCompareScalarExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar,
    VectorCompareOperator opType) : IGpuBoolVectorExpression, IBoolVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(vector);

    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(vector) || VectorEffects.MayThrow(scalar);

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
        VectorResult.Of(Apply(workspace, first.Int));

    // The flags of the capacity; the elements are the first Count of them, as for the input
    private BoolVectorEvaluation Apply(GpuWorkspace workspace, VectorEvaluation vecResult)
    {
        var scalarResult = scalar.Evaluate(workspace);
        try
        {
            var flags = _flags.Get(workspace, Capacity);

            // The scalar is read with its own word count and sign: it may be narrower or wider than the elements, and
            // it may be a view into a larger buffer whose following words are not 0.
            var ctx = new GpuBigIntCmpKernels.AotContexts.VectorCompare
            {
                Left = VectorCompareLaunch.Operand(vecResult.Storage),
                Right = VectorCompareLaunch.Scalar(scalarResult),
                Result = flags.ToProxy(),
                OpType = (int)opType
            };

            VectorCompareLaunch.Launch(workspace, ctx, Capacity, vecResult.Storage.WordsPerElement,
                scalarResult.Value.NumWords);
            return new BoolVectorEvaluation(flags, vecResult.Count);
        }
        finally
        {
            // An owned scalar result (a compound scalar expression) goes back to the pool; a leaf's is borrowed
            scalarResult.Dispose();
        }
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
