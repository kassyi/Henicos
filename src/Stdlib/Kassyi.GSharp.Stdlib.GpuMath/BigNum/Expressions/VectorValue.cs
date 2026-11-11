namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The result of a vector node as plain data: <see cref="VectorEvaluation"/>, <see cref="FloatVectorEvaluation"/> or
/// <see cref="BoolVectorEvaluation"/>, whichever the node produces.
/// </summary>
/// <param name="storage">The arrays of the result.</param>
/// <param name="count">The number of elements of the result.</param>
/// <param name="lease">Whether the arrays still hold the result.</param>
internal readonly struct VectorValue(VectorStorage storage, DeviceElementCount count, VectorLease lease = default)
{
    /// <summary>Gets the result of an integer node.</summary>
    public VectorEvaluation Int => new(storage.IntArray, count, lease);

    /// <summary>Gets the result of a floating-point node.</summary>
    public FloatVectorEvaluation Float => new(storage.FloatArray, count, lease);

    /// <summary>Gets the result of a flag node.</summary>
    public BoolVectorEvaluation Bool => new(storage.Words, count);

    /// <summary>Stores the result of an integer node.</summary>
    public static VectorValue Of(VectorEvaluation evaluation) =>
        new(VectorStorage.Of(evaluation.Storage), evaluation.Count, evaluation.Lease);

    /// <summary>Stores the result of a floating-point node.</summary>
    public static VectorValue Of(FloatVectorEvaluation evaluation) =>
        new(VectorStorage.Of(evaluation.Storage), evaluation.Count, evaluation.Lease);

    /// <summary>Stores the result of a flag node.</summary>
    public static VectorValue Of(BoolVectorEvaluation evaluation) =>
        new(VectorStorage.Of(evaluation.Flags), evaluation.Count);
}
