namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

internal readonly ref struct FloatVectorScalarOperands
{
    public FloatVectorScalarOperands(GpuBigFloatArrayView vector, GpuFloatOperand scalar, GpuBigFloatArrayView destination)
    {
        Vector = vector;
        Scalar = scalar;
        Destination = destination;
    }

    public GpuBigFloatArrayView Vector { get; }

    public GpuFloatOperand Scalar { get; }

    public GpuBigFloatArrayView Destination { get; }
}
