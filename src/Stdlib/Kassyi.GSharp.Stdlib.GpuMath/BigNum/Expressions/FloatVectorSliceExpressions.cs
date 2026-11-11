namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal static class FloatVectorSlice
{
    // The result of an empty window; out of line, so that its temporaries stay out of the frame of every level
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static VectorResult EmptyResult(GpuWorkspace workspace, int wordsPerElement,
        scoped in VectorRequest request) =>
        VectorResult.Of(new FloatVectorEvaluation(Empty(workspace, wordsPerElement, request.Target.FloatArray),
            DeviceElementCount.Static(0)));

    // An evaluation of no elements writes nothing, but a target of another width is still rejected, as by every node
    public static GpuBigFloatArrayView Empty(GpuWorkspace workspace, int wordsPerElement, GpuBigFloatArrayView targetResult)
    {
        if (VectorResultCache.HasTarget(targetResult))
        {
            VectorResultCache.TargetFloat(targetResult, 0, wordsPerElement);
        }

        return VectorResultCache.EmptyFloat(workspace, wordsPerElement);
    }
}
