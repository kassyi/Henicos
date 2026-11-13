namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>The same for a floating-point vector.</summary>
internal readonly ref struct FloatVectorEvaluation
{
    public FloatVectorEvaluation(GpuBigFloatArrayView storage, DeviceElementCount count, VectorLease lease = default)
    {
        Storage = storage;
        Count = count;
        Lease = lease;
    }

    public GpuBigFloatArrayView Storage { get; }

    public DeviceElementCount Count { get; }

    /// <inheritdoc cref="VectorEvaluation.Lease"/>
    public VectorLease Lease { get; }

    /// <summary>The number of elements to run a host loop over: the count, read when it is on the device.</summary>
    public int Elements(GpuWorkspace workspace) => Count.IsStatic ? Storage.Length : Count.Read(workspace);
}
