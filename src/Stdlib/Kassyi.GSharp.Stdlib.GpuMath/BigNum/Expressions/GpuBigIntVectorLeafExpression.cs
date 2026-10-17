namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal class GpuBigIntVectorLeafExpression(GpuBigIntArray array) : IGpuBigIntVectorExpression
{
    public int Length => array.Length;
    public int WordsPerElement => array.WordsPerElement;

    public GpuBigIntArray Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArray? targetResult = null)
    {
        var count = elementCount ?? (Length - elementOffset);
        if (elementOffset == 0 && count == Length)
        {
            return array;
        }

        return array.Slice(elementOffset, count);
    }

    public void Dispose()
    {
        // Does not own the array
    }
}
