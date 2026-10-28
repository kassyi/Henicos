namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// A leaf whose number of elements is a word in device memory, for tests: the storage holds the capacity, and only the
/// first <c>countWord[0]</c> elements are elements (design D3 6.4, allowed by T1). It lets the consumers of a device
/// count be tested before <c>Where</c> produces one.
/// </summary>
internal sealed class DeviceCountedLeafExpression(GpuBigIntArray storage, GpuBigUInt countWord)
    : IGpuBigIntVectorExpression, IDeviceCountedVector
{
    public int Length => throw new InvalidOperationException(DeviceCounted.LengthUnknown);

    public int Capacity => storage.Length;

    public int WordsPerElement => storage.WordsPerElement;

    public bool TryGetLength(out int length)
    {
        length = 0;
        return false;
    }

    public VectorEvaluation EvaluateCore(GpuWorkspace workspace, int elementOffset, int? elementCount,
        GpuBigUIntArrayView targetResult)
    {
        if (VectorResultCache.HasTarget(targetResult))
        {
            throw new NotSupportedException("The test leaf does not write a target.");
        }

        var count = elementCount ?? (Capacity - elementOffset);
        var whole = new DeviceElementCount(countWord.DevicePointer, 0, storage.Length);
        return new VectorEvaluation(storage.Slice(elementOffset, count), whole.SkipFirst(elementOffset).TakeFirst(count));
    }

    public GpuBigIntArrayView Evaluate(GpuWorkspace workspace, int elementOffset = 0, int? elementCount = null,
        GpuBigUIntArrayView targetResult = default) =>
        DeviceCounted.Materialize(workspace, EvaluateCore(workspace, elementOffset, elementCount, targetResult));

    public void Dispose()
    {
        // The storage and the count word belong to the test
    }
}
