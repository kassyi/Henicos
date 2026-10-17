namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal sealed class GpuBigFloatVectorLeafExpression(GpuBigFloatArray array) : IGpuBigFloatVectorExpression
{
    public int Length => array.Length;
    public int WordsPerElement => array.WordsPerElement;

    public GpuBigFloatArray Evaluate(
        GpuWorkspace workspace,
        int elementOffset = 0,
        int? elementCount = null,
        GpuBigFloatArray? targetResult = null)
    {
        var count = elementCount ?? (Length - elementOffset);
        var slice = (elementOffset == 0 && count == Length)
            ? array
            : array.Slice(elementOffset, count);

        if (targetResult != null)
        {
            var dest = targetResult.Value;
            for (var i = 0; i < count; i++)
            {
                dest.SetElement(i, slice.GetElement(i));
            }

            return dest;
        }

        return slice;
    }

    public void Dispose()
    {
        // Leaf expression does not own the source array.
    }
}
