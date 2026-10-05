namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A marker attribute instructing the GpuMath Source Generator to emit operator overloads and 
/// expression tree dispatch logic for a given GPU number type.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class GenerateGpuNumberOperatorsAttribute : Attribute;

/// <summary>
/// Represents a numeric type executed on the GPU, supporting deferred expression compilation via operator overloading.
/// </summary>
/// <typeparam name="TSelf">The concrete type implementing this interface.</typeparam>
public interface IGpuNumber<TSelf> : IEquatable<TSelf>, IComparable<TSelf>
    where TSelf : struct, IGpuNumber<TSelf>;
