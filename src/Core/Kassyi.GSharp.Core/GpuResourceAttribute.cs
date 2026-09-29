namespace Kassyi.GSharp.Core;

/// <summary>
/// Marks a class or struct as a GPU resource whose lifecycle must be managed via <see cref="IGpuResourceScope"/>.
/// This attribute is used by static code analysis to enforce proper disposal.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class GpuResourceAttribute : Attribute;