namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The result of evaluating a vector node for another node: storage of <see cref="DeviceElementCount.Capacity"/>
/// elements, of which only the first <c>Count</c> are elements.
/// </summary>
internal readonly ref struct VectorEvaluation
{
    public VectorEvaluation(GpuBigIntArrayView storage, DeviceElementCount count, VectorLease lease = default)
    {
        Storage = storage;
        Count = count;
        Lease = lease;
    }

    public GpuBigIntArrayView Storage { get; }

    public DeviceElementCount Count { get; }

    /// <summary>
    /// Gets whether <see cref="Storage"/> still holds this result (see <see cref="VectorOperandPair"/>).
    /// </summary>
    public VectorLease Lease { get; }
}
