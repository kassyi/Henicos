namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The result of a vector node, whichever kind it is: <see cref="VectorEvaluation"/> (values and signs),
/// <see cref="FloatVectorEvaluation"/> (significands, exponents and signs) or <see cref="BoolVectorEvaluation"/>
/// (flags). The levels of <see cref="VectorWalk"/> evaluated by recursion pass it on as it is; the walked levels keep
/// it as <see cref="VectorValue"/>.
/// </summary>
internal readonly ref struct VectorResult
{
    private readonly GpuBigUIntArrayView _first;
    private readonly GpuBigUIntArrayView _second;
    private readonly GpuBigUIntArrayView _third;

    public VectorResult(GpuBigUIntArrayView first, GpuBigUIntArrayView second, GpuBigUIntArrayView third,
        DeviceElementCount count, VectorLease lease)
    {
        _first = first;
        _second = second;
        _third = third;
        Count = count;
        Lease = lease;
    }

    /// <summary>Gets the first array: the values, the significands or the flags.</summary>
    public GpuBigUIntArrayView First => _first;

    /// <summary>Gets the second array: the signs of an integer vector, the exponents of a floating-point one.</summary>
    public GpuBigUIntArrayView Second => _second;

    /// <summary>Gets the third array: the signs of a floating-point vector.</summary>
    public GpuBigUIntArrayView Third => _third;

    /// <summary>Gets the number of elements.</summary>
    public DeviceElementCount Count { get; }

    /// <summary>Gets whether the arrays still hold the result.</summary>
    public VectorLease Lease { get; }

    /// <summary>Gets the result of an integer node.</summary>
    public VectorEvaluation Int => new(new GpuBigIntArrayView(_first, _second), Count, Lease);

    /// <summary>Gets the result of a floating-point node.</summary>
    public FloatVectorEvaluation Float => new(new GpuBigFloatArrayView(_first, _second, _third), Count, Lease);

    /// <summary>Gets the result of a flag node.</summary>
    public BoolVectorEvaluation Bool => new(_first, Count);

    /// <summary>The result of an integer node.</summary>
    public static VectorResult Of(VectorEvaluation evaluation) =>
        new(evaluation.Storage.Values, evaluation.Storage.Signs, default, evaluation.Count, evaluation.Lease);

    /// <summary>The result of a floating-point node.</summary>
    public static VectorResult Of(FloatVectorEvaluation evaluation) =>
        new(evaluation.Storage.Significands, evaluation.Storage.Exponents, evaluation.Storage.Signs, evaluation.Count,
            evaluation.Lease);

    /// <summary>The result of a flag node.</summary>
    public static VectorResult Of(BoolVectorEvaluation evaluation) =>
        new(evaluation.Flags, default, default, evaluation.Count, default);
}
