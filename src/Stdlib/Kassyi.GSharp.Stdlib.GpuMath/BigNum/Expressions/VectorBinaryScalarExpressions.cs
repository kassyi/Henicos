namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal abstract class VectorBinaryScalarExpressionBase(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    [TakesOwnership] IGpuBigIntExpression scalar,
    long bitsPerElement) : IGpuBigIntVectorExpression, IIntVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(vector);

    // Whether evaluating an input can throw: the vector, or a scalar that is not a value
    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(vector) || VectorEffects.MayThrow(scalar);

    public int Length => _shape.Length;
    public long BitsPerElement { get; } = bitsPerElement;

    public bool MayThrow => _inputsMayThrow || ElementMayThrow;

    /// <summary>
    /// Gets whether the operation itself can throw for an element (a zero divisor, a negative square root, a value the
    /// host arithmetic cannot compute).
    /// </summary>
    protected virtual bool ElementMayThrow => false;
    public int WordsPerElement { get; } = BigIntWidths.Words(bitsPerElement);

    // The result buffers, lent until the next evaluation or Dispose; a caller's target is never kept
    private readonly VectorResultCache _cache = new();

    protected uint BlockDimX => (uint)Math.Min(1024, WordsPerElement);

    public int Capacity => _shape.Capacity;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 1;

    public object GetOperand(int index) => vector;

    // The target is checked before the vector is evaluated, so that a rejected target evaluates nothing
    public void Enter(in VectorRequest request) => request.IntTarget(Capacity, WordsPerElement);

    // The window of the capacity; the elements are the first Count of them
    public VectorRequest OperandRequest(in VectorRequest request) => request.WindowOf(Capacity);

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second) =>
        VectorResult.Of(Apply(workspace, request.EvalCount(Capacity), request.IntTarget(Capacity, WordsPerElement),
            first.Int));

    private VectorEvaluation Apply(GpuWorkspace workspace, int evalCount, GpuBigUIntArrayView targetValues,
        VectorEvaluation vecResult)
    {
        var scalarResult = scalar.Evaluate(workspace);
        try
        {
            // A target that shares words with an input is written from the cache after the launch (#864)
            var output = VectorOutput.Open(_cache, workspace, evalCount, WordsPerElement, targetValues, vecResult.Storage,
                scalar: scalarResult.Value.Handle);
            var operands = new VectorScalarOperands(vecResult.Storage, scalarResult, output.Destination);
            // An empty vector launches nothing (a grid of 0 blocks is an invalid launch), nor does a zero divisor over
            // no element; neither writes an element
            var launches = evalCount > 0 && !SkipsZeroDivision(workspace, operands, vecResult.Count);
            if (launches)
            {
                DispatchKernel(workspace, operands, (uint)evalCount);
            }

            var written = DeviceElementCount.Static(launches ? evalCount : 0);
            return new VectorEvaluation(output.Close(workspace, written), vecResult.Count, _cache.Lease);
        }
        finally
        {
            // An owned scalar result (a compound scalar expression) goes back to the pool; a leaf's is borrowed
            scalarResult.Dispose();
        }
    }

    protected GpuBigIntVectorKernels.AotContexts.VectorBinaryOperation CreateScalarOp(VectorScalarOperands operands) =>
        new()
        {
            Left = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
            {
                Limbs = operands.Vector.Values.ToProxy(),
                Signs = operands.Vector.Signs.ToProxy(),
                NumWords = vector.WordsPerElement
            },
            Right = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
            {
                Limbs = operands.Scalar.Value.ToProxy(),
                Signs = operands.Vector.Signs.ToProxy(),
                NumWords = operands.Scalar.Value.NumWords
            },
            Result = new GpuBigIntVectorKernels.AotContexts.SignedVectorOutput
            {
                Limbs = operands.Destination.Values.ToProxy(),
                Signs = operands.Destination.Signs.ToProxy(),
                NumWords = WordsPerElement
            },
            ScalarSign = operands.Scalar.IsNegative ? 1u : 0u,
            IsScalar = true
        };

    // Whether a zero divisor ends the evaluation with nothing divided; it throws when the vector has an element
    private bool SkipsZeroDivision(GpuWorkspace workspace, VectorScalarOperands operands, DeviceElementCount count)
    {
        if (!DividesByZero(workspace, operands))
        {
            return false;
        }

        ZeroDivisorCheck.ThrowIfAnyElement(workspace, count);
        return true;
    }

    /// <summary>
    /// Gets whether the operation is a division whose scalar divisor is zero (one read of the scalar).
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// A division, while the workspace stream captures a CUDA graph.
    /// </exception>
    protected virtual bool DividesByZero(GpuWorkspace workspace, VectorScalarOperands operands) => false;

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        VectorScalarOperands operands,
        uint gridDimX);

    // One level of a recursive evaluation (VectorWalk.EvaluateDirect)
    public VectorResult EvaluateDirect(GpuWorkspace workspace, scoped in VectorRequest request,
        RecursionLevel level)
    {
        Enter(request);

        return Apply(workspace, request, VectorWalk.EvaluateDirect(vector, workspace, OperandRequest(request), level),
            default);
    }

    public void DisposeOwn() => _cache.Dispose();

    public void Dispose() => VectorWalk.Dispose(this);
}
