namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

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

            var i = 0;
            for (; i < evalCount; i++)
            {
                var srcElem = source.Values.GetElement(i);
                if (srcSigns[i] != 0 && !srcElem.IsZero())
                {
                    var ex = new InvalidOperationException("Cannot compute square root of a negative integer.");
                    if (PendingThrows.TryRecord(i, ex))
                    {
                        break;
                    }

                    throw ex;
                }

                var dstElem = destination.Values.GetElement(i);
                GpuBigIntDivRem.Sqrt(srcElem, dstElem);
            }

            if (i < evalCount)
            {
                var tail = destination.Values.Slice(i, evalCount - i);
                workspace.Provider.ClearMemoryAsync(tail.DevicePointer, tail.TotalWords, workspace.Stream);
            }

            workspace.Provider.ClearMemoryAsync(destination.Signs.DevicePointer, evalCount, workspace.Stream);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(srcSigns);
        }
    }
}
