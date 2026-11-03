namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class GpuBigIntVectorLeafExpression(GpuBigIntArray array) : IGpuBigIntVectorExpression
{
    public int Length => array.Length;
    public int WordsPerElement => array.WordsPerElement;

    // The signs, kept when a target overwrites the input's own signs (#864); allocated only then
    private readonly VectorResultCache _cache = new();

    public GpuBigIntArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArrayView targetResult = default)
    {
        DeviceCounted.ThrowIfCpuFallback(workspace);
        var count = elementCount ?? (Length - elementOffset);

        // The user's array is lent as a view, never handed out as an owner (design 4.6)
        var slice = array.Slice(elementOffset, count);
        if (!VectorResultCache.HasTarget(targetResult))
        {
            return slice;
        }

        // As every node: a target is checked, its first elements receive the values, and the result is a view of them
        // with the signs of the input
        var values = VectorResultCache.TargetValues(targetResult, count, WordsPerElement);
        var signs = slice.Signs;
        if (DeviceRanges.Overlaps(values, slice.Signs))
        {
            // A target over the input's own signs (1-word elements) would overwrite the signs the result returns: they
            // are copied into the cache first
            var cachedSigns = _cache.Signs(workspace, count);
            VectorResultCache.CopyValues(workspace, cachedSigns, slice.Signs);
            signs = cachedSigns;
        }

        // A target that overlaps the array (a shifted view of it) is copied through a temporary
        VectorResultCache.CopyValues(workspace, values, slice.Values);

        return new GpuBigIntArrayView(values, signs);
    }

    public void Dispose()
    {
        // Does not own the array; returns only the signs it cached
        _cache.Dispose();
    }
}
