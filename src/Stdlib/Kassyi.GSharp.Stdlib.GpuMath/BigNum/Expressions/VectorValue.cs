namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// A <see cref="VectorResult"/> as plain data, for the stacks of the walked levels of <see cref="VectorWalk"/>, where a
/// <see langword="ref struct"/> cannot wait. Converting back gives the same result.
/// </summary>
internal readonly struct VectorValue
{
    private readonly StoredView _first;
    private readonly StoredView _second;
    private readonly StoredView _third;
    private readonly DeviceElementCount _count;
    private readonly VectorLease _lease;

    private VectorValue(StoredView first, StoredView second, StoredView third, DeviceElementCount count,
        VectorLease lease)
    {
        _first = first;
        _second = second;
        _third = third;
        _count = count;
        _lease = lease;
    }

    /// <summary>Gets the result again.</summary>
    public VectorResult Result => new(_first.View, _second.View, _third.View, _count, _lease);

    /// <summary>Stores a result.</summary>
    public static VectorValue Of(VectorResult result) =>
        new(StoredView.Of(result.First), StoredView.Of(result.Second), StoredView.Of(result.Third), result.Count,
            result.Lease);
}
