namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class GpuBigFloatVectorLeafExpression(GpuBigFloatArray array) : IGpuBigFloatVectorExpression
{
    public int Length => array.Length;
    public int WordsPerElement => array.WordsPerElement;

    public GpuBigFloatArrayView Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArrayView targetResult = default)
    {
        var count = elementCount ?? (Length - elementOffset);

        // The user's array is lent as a view, never handed out as an owner (design 4.6)
        var slice = array.Slice(elementOffset, count);
        if (!VectorResultCache.HasTarget(targetResult))
        {
            return slice;
        }

        // As every node: a target is checked, and its first elements receive the values
        var destination = VectorResultCache.TargetFloat(targetResult, count, WordsPerElement);
        // A target that overlaps the array (a shifted view of it) is copied through a temporary, component by component.
        // A target component over another component of the input (significands over exponents) is not handled: the
        // public API cannot build one (a float view of arbitrary components needs the internal constructor)
        return VectorResultCache.Deliver(workspace, slice, destination);
    }

    public void Dispose()
    {
        // Does not own the source array
    }
}
