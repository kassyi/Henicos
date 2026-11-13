namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>The same for a vector of flags (1 = true, 0 = false).</summary>
internal readonly ref struct BoolVectorEvaluation
{
    public BoolVectorEvaluation(GpuBigUIntArrayView flags, DeviceElementCount count)
    {
        Flags = flags;
        Count = count;
    }

    public GpuBigUIntArrayView Flags { get; }

    public DeviceElementCount Count { get; }
}
