namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly ref struct FloatVectorZipOperands
{
    public FloatVectorZipOperands(
        GpuBigFloatArrayView left,
        GpuBigFloatArrayView right,
        GpuBigFloatArrayView destination,
        GpuBigUIntArrayView zeroDivisor)
    {
        Left = left;
        Right = right;
        Destination = destination;
        ZeroDivisor = zeroDivisor;
    }

    public GpuBigFloatArrayView Left { get; }

    public GpuBigFloatArrayView Right { get; }

    public GpuBigFloatArrayView Destination { get; }

    /// <summary>Gets the word of <see cref="ZeroDivisorCheck"/> for a division, or the default view.</summary>
    public GpuBigUIntArrayView ZeroDivisor { get; }
}
