namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal abstract class VectorUnaryExpressionBase(
    [TakesOwnership] IGpuBigIntVectorExpression vector) : IGpuBigIntVectorExpression, IIntVectorOperator
{
    private readonly VectorShape _shape = VectorShape.Of(vector);

    public int Length => _shape.Length;
    public int WordsPerElement { get; } = vector.WordsPerElement;

    // The result buffers, lent until the next evaluation or Dispose; a caller's target is never kept
    private readonly VectorResultCache _cache = new();

    public int Capacity => _shape.Capacity;

    public bool TryGetLength(out int length) => _shape.TryGetLength(out length);

    public int OperandCount => 1;

    public object GetOperand(int index) => vector;

    // The target is checked before the vector is evaluated, so that a rejected target evaluates nothing
    public void Enter(in VectorRequest request)
    {
        ValidateBeforeEvaluate();
        request.IntTarget(Capacity, WordsPerElement);
    }

    // The window of the capacity; the elements are the first Count of them
    public VectorRequest OperandRequest(in VectorRequest request) =>
        VectorRequest.Window(request.Offset, request.EvalCount(Capacity));

    public VectorValue Apply(GpuWorkspace workspace, in VectorRequest request, in VectorValue first,
        in VectorValue second) =>
        VectorValue.Of(Apply(workspace, request.EvalCount(Capacity), request.IntTarget(Capacity, WordsPerElement),
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

    protected virtual void ValidateBeforeEvaluate()
    {
    }

    protected abstract void EvaluateElements(
        GpuWorkspace workspace,
        GpuBigIntArrayView source,
        GpuBigIntArrayView destination,
        int evalCount);

    public void DisposeOwn() => _cache.Dispose();

    // A cache release that throws (a failed capture refuses the clear) does not skip the input
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

internal sealed class VectorSqrtExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector) : VectorUnaryExpressionBase(vector)
{
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
    protected override void ValidateBeforeEvaluate()
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }
    }

    protected override void EvaluateElements(
        GpuWorkspace workspace,
        GpuBigIntArrayView source,
        GpuBigIntArrayView destination,
        int evalCount)
    {
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
