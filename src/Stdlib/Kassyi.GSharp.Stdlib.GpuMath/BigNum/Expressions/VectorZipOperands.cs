namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly ref struct VectorZipOperands
{
    public VectorZipOperands(
        GpuBigIntArrayView left,
        GpuBigIntArrayView right,
        GpuBigIntArrayView destination,
        GpuBigUIntArrayView zeroDivisor)
    {
        Left = left;
        Right = right;
        Destination = destination;
        ZeroDivisor = zeroDivisor;
    }

    public GpuBigIntArrayView Left { get; }

    public GpuBigIntArrayView Right { get; }

    public GpuBigIntArrayView Destination { get; }

    /// <summary>Gets the word of <see cref="ZeroDivisorCheck"/> for a division, or the default view.</summary>
    public GpuBigUIntArrayView ZeroDivisor { get; }
}
