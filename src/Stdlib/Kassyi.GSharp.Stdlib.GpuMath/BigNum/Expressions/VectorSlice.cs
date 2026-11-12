namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal static class VectorSlice
{
    // The result of an empty window; out of line, so that its temporaries stay out of the frame of every level
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static VectorResult EmptyResult(GpuWorkspace workspace, int wordsPerElement,
        scoped in VectorRequest request) =>
        VectorResult.Of(new VectorEvaluation(Empty(workspace, wordsPerElement, request.Target.Words),
            DeviceElementCount.Static(0)));

    // An evaluation of no elements writes nothing, but a target of another width is still rejected, as by every node
    public static GpuBigIntArrayView Empty(GpuWorkspace workspace, int wordsPerElement, GpuBigUIntArrayView targetResult)
    {
        if (VectorResultCache.HasTarget(targetResult))
        {
            VectorResultCache.TargetValues(targetResult, 0, wordsPerElement);
        }

        return VectorResultCache.EmptySigned(workspace, wordsPerElement);
    }
}
