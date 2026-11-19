namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class VectorSqrtExpression(
    [TakesOwnership] IGpuBigIntVectorExpression vector) : VectorUnaryExpressionBase(vector)
{
    private const string NegativeMessage = "Cannot compute square root of a negative integer.";

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
                    // Under a scope the element is recorded and the elements from it on are not computed
                    PendingThrows.RecordOrThrow(i, new InvalidOperationException(NegativeMessage));
                    break;
                }

                GpuBigIntDivRem.Sqrt(srcElem, destination.Values.GetElement(i));
            }

            workspace.Provider.ClearMemoryAsync(destination.Signs.DevicePointer, evalCount, workspace.Stream);
            RecordedTail.Clear(workspace, destination, i, evalCount);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(srcSigns);
        }
    }
}
