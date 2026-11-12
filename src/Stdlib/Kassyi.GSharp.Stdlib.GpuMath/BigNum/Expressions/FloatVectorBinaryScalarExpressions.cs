namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal abstract class FloatVectorBinaryScalarExpressionBase(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    GpuFloatOperand scalar,
    int wordsPerElement) : IGpuBigFloatVectorExpression, IFloatVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(vector);

    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(vector);

    public int Length => _shape.Length;
    public int WordsPerElement { get; } = wordsPerElement;

    public bool MayThrow => _inputsMayThrow || ElementMayThrow;

    /// <summary>
    /// Gets whether the operation itself can throw for an element (a zero divisor, a negative square root, a value the
    /// host arithmetic cannot compute).
    /// </summary>
    protected virtual bool ElementMayThrow => RunsOnHost;

    // The result buffers, lent until the next evaluation or Dispose; a caller's target is never kept
    private readonly VectorResultCache _cache = new();

    protected uint BlockDimX => (uint)Math.Min(1024, WordsPerElement);

    public int Capacity => _shape.Capacity;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 1;

    public object GetOperand(int index) => vector;

    // The target is checked before the vector is evaluated, so that a rejected target evaluates nothing
    public void Enter(in VectorRequest request) => request.FloatTarget(Capacity, WordsPerElement);

    // The window of the capacity; the elements are the first Count of them
    public VectorRequest OperandRequest(in VectorRequest request) => request.WindowOf(Capacity);

    public VectorResult Apply(GpuWorkspace workspace, scoped in VectorRequest request, VectorResult first,
        VectorResult second) =>
        VectorResult.Of(Apply(workspace, request.EvalCount(Capacity), request.FloatTarget(Capacity, WordsPerElement),
            first.Float));

    private FloatVectorEvaluation Apply(GpuWorkspace workspace, int evalCount, GpuBigFloatArrayView target,
        FloatVectorEvaluation vecResult)
    {
        // A target that shares words with an input is written from the cache after the launch (#864)
        var output = VectorOutput.Open(_cache, workspace, evalCount, WordsPerElement, target, vecResult.Storage,
            scalar: scalar.Significand);
        var operands = new FloatVectorScalarOperands(vecResult.Storage, scalar, output.Destination);
        // A zero divisor over no element divides and writes nothing; over an element it throws
        if (evalCount > 0 && DividesByZero(workspace, operands))
        {
            ZeroDivisorCheck.ThrowIfAnyElement(workspace, vecResult.Count);
            return new FloatVectorEvaluation(output.Close(workspace, DeviceElementCount.Static(0)), vecResult.Count,
                _cache.Lease);
        }

        // An empty vector launches nothing: a grid of 0 blocks is an invalid launch.
        var elements = RunsOnHost ? vecResult.Elements(workspace) : evalCount;
        if (elements > 0)
        {
            DispatchKernel(workspace, operands, (uint)elements);
        }

        // A host loop wrote only the elements; a kernel wrote the whole window
        var written = RunsOnHost ? vecResult.Count : DeviceElementCount.Static(evalCount);
        return new FloatVectorEvaluation(output.Close(workspace, written), vecResult.Count, _cache.Lease);
    }

    /// <summary>
    /// Whether the operation runs on the host element by element (Pow): it then runs over the elements only, not the
    /// rest of the capacity, which costs one synchronization when the count is on the device.
    /// </summary>
    protected virtual bool RunsOnHost => false;

    /// <summary>
    /// Gets whether the operation is a division whose scalar divisor is zero (one read of the scalar).
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// A division, while the workspace stream captures a CUDA graph.
    /// </exception>
    protected virtual bool DividesByZero(GpuWorkspace workspace, FloatVectorScalarOperands operands) => false;

    protected static GpuBigIntVectorKernels.AotContexts.VectorFloatBinaryOperation CreateScalarFloatOp(FloatVectorScalarOperands operands)
    {
        var scalarSig = operands.Scalar.Significand;
        return new GpuBigIntVectorKernels.AotContexts.VectorFloatBinaryOperation
        {
            Core = new GpuBigIntVectorKernels.AotContexts.VectorBinaryOperation
            {
                Left = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
                {
                    Limbs = operands.Vector.Significands.ToProxy(),
                    Signs = operands.Vector.Signs.ToProxy(),
                    NumWords = operands.Vector.WordsPerElement
                },
                Right = new GpuBigIntVectorKernels.AotContexts.SignedVectorOperandReadOnly
                {
                    Limbs = scalarSig.ToProxy(),
                    Signs = operands.Vector.Signs.ToProxy(),
                    NumWords = scalarSig.NumWords
                },
                Result = new GpuBigIntVectorKernels.AotContexts.SignedVectorOutput
                {
                    Limbs = operands.Destination.Significands.ToProxy(),
                    Signs = operands.Destination.Signs.ToProxy(),
                    NumWords = operands.Destination.WordsPerElement
                },
                ScalarSign = operands.Scalar.IsNegative ? 1u : 0u,
                IsScalar = true
            },
            Exponents = new GpuBigIntVectorKernels.AotContexts.FloatVectorExponents
            {
                ExpA = operands.Vector.Exponents.ToProxy(),
                ExpB = operands.Vector.Exponents.ToProxy(),
                ExpResult = operands.Destination.Exponents.ToProxy(),
                ScalarExp = operands.Scalar.Exponent
            }
        };
    }

    protected abstract void DispatchKernel(
        GpuWorkspace workspace,
        FloatVectorScalarOperands operands,
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
