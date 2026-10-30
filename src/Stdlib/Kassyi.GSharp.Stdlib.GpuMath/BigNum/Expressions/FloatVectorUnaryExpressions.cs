namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;
using Providers;

internal enum FloatVectorUnaryOpKind
{
    Sqrt,
    Rsqrt,
    Reciprocal,
    Exp,
    Log
}

internal static class FloatVectorElementwiseHelper
{
    internal delegate GpuFloatResult UnaryElementOp(
        ref GpuBigIntExecutionContext context,
        IGpuBigFloatArithmetic arithmetic,
        in GpuFloatOperand source,
        GpuBigIntMemoryHandle destination);

    internal delegate GpuFloatResult BinaryElementOp(
        ref GpuBigIntExecutionContext context,
        IGpuBigFloatArithmetic arithmetic,
        in GpuFloatOperand left,
        in GpuFloatOperand right,
        GpuBigIntMemoryHandle destination);

    public static void ExecuteUnary(
        GpuWorkspace workspace,
        GpuBigFloatArrayView source,
        GpuBigFloatArrayView destination,
        int count,
        UnaryElementOp operation)
    {
        if (count == 0)
        {
            return;
        }

        var arithmetic = GpuWorkspaceArithmetic.FloatArithmetic(workspace);
        var execContext = GpuWorkspaceArithmetic.CreateContext(workspace);

        var srcExps = ArrayPool<uint>.Shared.Rent(count);
        var srcSigns = ArrayPool<uint>.Shared.Rent(count);
        var dstExps = ArrayPool<uint>.Shared.Rent(count);
        var dstSigns = ArrayPool<uint>.Shared.Rent(count);
        try
        {
            workspace.Provider.CopyDeviceToHost(srcExps, source.Exponents.DevicePointer, count, workspace.Stream);
            workspace.Provider.CopyDeviceToHost(srcSigns, source.Signs.DevicePointer, count, workspace.Stream);

            for (var i = 0; i < count; i++)
            {
                var srcSig = source.Significands.GetElement(i);
                var dstSig = destination.Significands.GetElement(i);
                var operand = new GpuFloatOperand(srcSig.Handle, unchecked((int)srcExps[i]), srcSigns[i] != 0);

                var res = operation(ref execContext, arithmetic, in operand, dstSig.Handle);
                dstExps[i] = unchecked((uint)res.Exponent);
                dstSigns[i] = res.IsNegative ? 1u : 0u;
            }

            workspace.Provider.CopyHostToDevice(destination.Exponents.DevicePointer, dstExps, count, workspace.Stream);
            workspace.Provider.CopyHostToDevice(destination.Signs.DevicePointer, dstSigns, count, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(srcExps);
            ArrayPool<uint>.Shared.Return(srcSigns);
            ArrayPool<uint>.Shared.Return(dstExps);
            ArrayPool<uint>.Shared.Return(dstSigns);
        }
    }

    public static void ExecuteBinary(
        GpuWorkspace workspace,
        GpuBigFloatArrayView left,
        GpuBigFloatArrayView right,
        GpuBigFloatArrayView destination,
        int count,
        BinaryElementOp operation)
    {
        if (count == 0)
        {
            return;
        }

        var arithmetic = GpuWorkspaceArithmetic.FloatArithmetic(workspace);
        var execContext = GpuWorkspaceArithmetic.CreateContext(workspace);

        var leftExps = ArrayPool<uint>.Shared.Rent(count);
        var leftSigns = ArrayPool<uint>.Shared.Rent(count);
        var rightExps = ArrayPool<uint>.Shared.Rent(count);
        var rightSigns = ArrayPool<uint>.Shared.Rent(count);
        var dstExps = ArrayPool<uint>.Shared.Rent(count);
        var dstSigns = ArrayPool<uint>.Shared.Rent(count);
        try
        {
            workspace.Provider.CopyDeviceToHost(leftExps, left.Exponents.DevicePointer, count, workspace.Stream);
            workspace.Provider.CopyDeviceToHost(leftSigns, left.Signs.DevicePointer, count, workspace.Stream);
            workspace.Provider.CopyDeviceToHost(rightExps, right.Exponents.DevicePointer, count, workspace.Stream);
            workspace.Provider.CopyDeviceToHost(rightSigns, right.Signs.DevicePointer, count, workspace.Stream);

            for (var i = 0; i < count; i++)
            {
                var leftSig = left.Significands.GetElement(i);
                var rightSig = right.Significands.GetElement(i);
                var dstSig = destination.Significands.GetElement(i);

                var leftOp = new GpuFloatOperand(leftSig.Handle, unchecked((int)leftExps[i]), leftSigns[i] != 0);
                var rightOp = new GpuFloatOperand(rightSig.Handle, unchecked((int)rightExps[i]), rightSigns[i] != 0);

                var res = operation(ref execContext, arithmetic, in leftOp, in rightOp, dstSig.Handle);
                dstExps[i] = unchecked((uint)res.Exponent);
                dstSigns[i] = res.IsNegative ? 1u : 0u;
            }

            workspace.Provider.CopyHostToDevice(destination.Exponents.DevicePointer, dstExps, count, workspace.Stream);
            workspace.Provider.CopyHostToDevice(destination.Signs.DevicePointer, dstSigns, count, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(leftExps);
            ArrayPool<uint>.Shared.Return(leftSigns);
            ArrayPool<uint>.Shared.Return(rightExps);
            ArrayPool<uint>.Shared.Return(rightSigns);
            ArrayPool<uint>.Shared.Return(dstExps);
            ArrayPool<uint>.Shared.Return(dstSigns);
        }
    }
}

internal abstract class FloatVectorUnaryExpressionBase(
    [TakesOwnership] IGpuBigFloatVectorExpression vector) : IGpuBigFloatVectorExpression, IDeviceCountedFloatVector
{
    public int Length => vector.Length;
    public int WordsPerElement => vector.WordsPerElement;

    // The result buffers, lent until the next evaluation or Dispose; a caller's target is never kept (design 4.6)
    private readonly VectorResultCache _cache = new();

    public int Capacity => vector.Capacity;

    public bool TryGetLength(out int length) => vector.TryGetLength(out length);

    // Evaluates the window of the capacity; the elements are the first Count of them (design D3 4.3)
    public FloatVectorEvaluation EvaluateCore(
        GpuWorkspace workspace,
        int elementOffset,
        int? elementCount,
        GpuBigFloatArrayView targetResult)
    {
        ValidateBeforeEvaluate();

        var evalCount = elementCount ?? (Capacity - elementOffset);
        // The target is checked before the child runs, so that a rejected target evaluates nothing
        var target = VectorResultCache.HasTarget(targetResult)
            ? VectorResultCache.TargetFloat(targetResult, evalCount, WordsPerElement)
            : default;
        var vecResult = DeviceCounted.Evaluate(vector, workspace, elementOffset, evalCount);

        // A target that shares words with the input is written from the cache after the loop: the loop writes element i
        // before it reads element i + 1 (#864)
        var output = VectorOutput.Open(_cache, workspace, evalCount, WordsPerElement, target, vecResult.Storage);

        // The host loop runs over the elements only: a value after the count (the zero padding of a Where) could make
        // an element operation throw (Log(0))
        FloatVectorElementwiseHelper.ExecuteUnary(workspace, vecResult.Storage, output.Destination,
            vecResult.Elements(workspace), EvaluateElement);
        return new FloatVectorEvaluation(output.Close(workspace, vecResult.Count), vecResult.Count);
    }

    protected virtual void ValidateBeforeEvaluate()
    {
    }

    protected abstract GpuFloatResult EvaluateElement(
        ref GpuBigIntExecutionContext context,
        IGpuBigFloatArithmetic arithmetic,
        in GpuFloatOperand source,
        GpuBigIntMemoryHandle destination);

    public void Dispose()
    {
        // A cache release that throws (a failed capture refuses the clear) does not skip the children
        try
        {
            _cache.Dispose();
        }
        finally
        {
            vector.Dispose();
        }
    }
}

internal sealed class FloatVectorUnaryExpression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    FloatVectorUnaryOpKind opKind) : FloatVectorUnaryExpressionBase(vector)
{
    protected override GpuFloatResult EvaluateElement(
        ref GpuBigIntExecutionContext context,
        IGpuBigFloatArithmetic arithmetic,
        in GpuFloatOperand source,
        GpuBigIntMemoryHandle destination) =>
        opKind switch
        {
            FloatVectorUnaryOpKind.Sqrt => arithmetic.Sqrt(ref context, in source, destination),
            FloatVectorUnaryOpKind.Rsqrt => arithmetic.Rsqrt(ref context, in source, destination),
            FloatVectorUnaryOpKind.Reciprocal => arithmetic.Reciprocal(ref context, in source, destination),
            FloatVectorUnaryOpKind.Exp => arithmetic.Exp(ref context, in source, destination),
            FloatVectorUnaryOpKind.Log => arithmetic.Log(ref context, in source, destination),
            _ => throw new ArgumentOutOfRangeException(nameof(opKind))
        };
}

internal sealed class FloatVectorDivByUInt32Expression(
    [TakesOwnership] IGpuBigFloatVectorExpression vector,
    uint divisor) : FloatVectorUnaryExpressionBase(vector)
{
    protected override void ValidateBeforeEvaluate()
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }
    }

    protected override GpuFloatResult EvaluateElement(
        ref GpuBigIntExecutionContext context,
        IGpuBigFloatArithmetic arithmetic,
        in GpuFloatOperand source,
        GpuBigIntMemoryHandle destination)
    {
        var res = arithmetic.DivideByUInt32(ref context, in source, divisor, destination);
        return new GpuFloatResult(res.Exponent, res.IsNegative);
    }
}
