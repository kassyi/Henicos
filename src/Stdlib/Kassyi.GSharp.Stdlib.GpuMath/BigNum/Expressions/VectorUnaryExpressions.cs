namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal abstract class VectorUnaryExpressionBase(
    [TakesOwnership] IGpuBigIntVectorExpression vector) : IGpuBigIntVectorExpression, IIntVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(vector);

    private readonly bool _inputsMayThrow = VectorEffects.MayThrow(vector);

    public int Length => _shape.Length;
    public int WordsPerElement { get; } = vector.WordsPerElement;

    public bool MayThrow => _inputsMayThrow || ElementMayThrow;

    /// <summary>
    /// Gets whether the operation itself can throw for an element (a zero divisor, a negative square root, a value the
    /// host arithmetic cannot compute).
    /// </summary>
    protected abstract bool ElementMayThrow { get; }

    // The result buffers, lent until the next evaluation or Dispose; a caller's target is never kept
    private readonly VectorResultCache _cache = new();

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
        // A target that shares words with the input is written from the cache after the loop: the loop writes element i
        // before it reads element i + 1 (#864)
        var output = VectorOutput.Open(_cache, workspace, evalCount, WordsPerElement, targetValues, vecResult.Storage);

        // The elements run on the host one by one: only the elements, not the rest of the capacity, so that a value
        // after the count cannot throw (a negative square root). A device count costs one synchronization here.
        var elements = vecResult.Count.IsStatic ? evalCount : vecResult.Count.Read(workspace);
        if (elements > 0)
        {
            EvaluateElements(workspace, vecResult.Storage, output.Destination, elements);
        }

        return new VectorEvaluation(output.Close(workspace, vecResult.Count), vecResult.Count, _cache.Lease);
    }

    protected abstract void EvaluateElements(
        GpuWorkspace workspace,
        GpuBigIntArrayView source,
        GpuBigIntArrayView destination,
        int evalCount);

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

internal sealed class VectorSqrtExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector) : VectorUnaryExpressionBase(vector)
{
    protected override bool ElementMayThrow => true;

    protected override void EvaluateElements(
        GpuWorkspace workspace,
        GpuBigIntArrayView source,
        GpuBigIntArrayView destination,
        int evalCount)
    {
        var srcSigns = ArrayPool<uint>.Shared.Rent(evalCount);
        try
        {
            workspace.Provider.CopyDeviceToHost(
                srcSigns, source.Signs.DevicePointer, evalCount, workspace.Stream);

            for (var i = 0; i < evalCount; i++)
            {
                var srcElem = source.Values.GetElement(i);
                if (srcSigns[i] != 0 && !srcElem.IsZero())
                {
                    throw new InvalidOperationException("Cannot compute square root of a negative integer.");
                }

                var dstElem = destination.Values.GetElement(i);
                GpuBigIntDivRem.Sqrt(srcElem, dstElem);
            }

            workspace.Provider.ClearMemoryAsync(destination.Signs.DevicePointer, evalCount, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(srcSigns);
        }
    }
}

internal sealed class VectorDivByUInt32Expression(
    [TakesOwnership] IGpuBigIntVectorExpression vector,
    uint divisor) : VectorUnaryExpressionBase(vector)
{
    protected override bool ElementMayThrow => divisor == 0;

    // Runs only when there is an element (evalCount > 0): C# divides only the elements, so a zero divisor over none
    // divides nothing, as System.Linq
    protected override void EvaluateElements(
        GpuWorkspace workspace,
        GpuBigIntArrayView source,
        GpuBigIntArrayView destination,
        int evalCount)
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }

        var srcSigns = ArrayPool<uint>.Shared.Rent(evalCount);
        var dstSigns = ArrayPool<uint>.Shared.Rent(evalCount);
        try
        {
            workspace.Provider.CopyDeviceToHost(
                srcSigns, source.Signs.DevicePointer, evalCount, workspace.Stream);

            for (var i = 0; i < evalCount; i++)
            {
                var srcElem = source.Values.GetElement(i);
                var dstElem = destination.Values.GetElement(i);
                GpuBigIntMath.DivideByUInt32(srcElem, divisor, dstElem);

                dstSigns[i] = (srcSigns[i] != 0 && !dstElem.IsZero()) ? 1u : 0u;
            }

            workspace.Provider.CopyHostToDevice(
                destination.Signs.DevicePointer, dstSigns, evalCount, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(srcSigns);
            ArrayPool<uint>.Shared.Return(dstSigns);
        }
    }
}
