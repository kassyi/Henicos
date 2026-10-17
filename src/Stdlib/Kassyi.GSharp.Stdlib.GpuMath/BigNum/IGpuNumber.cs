namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// Represents a self-typed numeric value executed on the GPU, supporting value equality and total ordering.
/// </summary>
/// <typeparam name="TSelf">The concrete value type implementing this interface.</typeparam>
public interface IGpuNumber<TSelf> : IGpuBigNumberBase, IEquatable<TSelf>, IComparable<TSelf>
    where TSelf : struct, IGpuNumber<TSelf>;
