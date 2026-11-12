namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The arrays of an evaluated vector or of a caller's target, as plain data: the values and the signs of an integer
/// vector, the significands, the exponents and the signs of a floating-point vector, or the words of a flag vector or
/// of an integer target.
/// </summary>
internal readonly struct VectorStorage
{
    private readonly StoredView _first;
    private readonly StoredView _second;
    private readonly StoredView _third;

    private VectorStorage(StoredView first, StoredView second = default, StoredView third = default)
    {
        _first = first;
        _second = second;
        _third = third;
    }

    /// <summary>Gets the integer array.</summary>
    public GpuBigIntArrayView IntArray => new(_first.View, _second.View);

    /// <summary>Gets the floating-point array.</summary>
    public GpuBigFloatArrayView FloatArray => new(_first.View, _second.View, _third.View);

    /// <summary>Gets the words of a flag vector or of an integer target.</summary>
    public GpuBigUIntArrayView Words => _first.View;

    /// <summary>Stores an integer array.</summary>
    public static VectorStorage Of(GpuBigIntArrayView array) =>
        new(StoredView.Of(array.Values), StoredView.Of(array.Signs));

    /// <summary>Stores a floating-point array.</summary>
    public static VectorStorage Of(GpuBigFloatArrayView array) =>
        new(StoredView.Of(array.Significands), StoredView.Of(array.Exponents), StoredView.Of(array.Signs));

    /// <summary>Stores the words of a flag vector or of an integer target.</summary>
    public static VectorStorage Of(GpuBigUIntArrayView words) => new(StoredView.Of(words));
}
