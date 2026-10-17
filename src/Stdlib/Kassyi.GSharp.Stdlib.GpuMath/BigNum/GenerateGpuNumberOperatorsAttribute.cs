namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A marker attribute instructing the GpuMath Source Generator to emit operator overloads and
/// expression tree dispatch logic for a given GPU number type.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class GenerateGpuNumberOperatorsAttribute : Attribute;
