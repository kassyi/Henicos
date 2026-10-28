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
        if (count > 0)
        {
            var provider = workspace.Provider;
            provider.CopyDeviceToDevice(destination.Significands.DevicePointer, slice.Significands.DevicePointer,
                destination.Significands.TotalWords, workspace.Stream);
            provider.CopyDeviceToDevice(destination.Exponents.DevicePointer, slice.Exponents.DevicePointer, count,
                workspace.Stream);
            provider.CopyDeviceToDevice(destination.Signs.DevicePointer, slice.Signs.DevicePointer, count,
                workspace.Stream);
        }

        return destination;
    }

    public void Dispose()
    {
        // Does not own the source array
    }
}
