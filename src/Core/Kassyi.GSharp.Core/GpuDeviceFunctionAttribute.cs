namespace Kassyi.GSharp.Core;

/// <summary>
/// Marks a C# method as a callable GPU device function (<c>__device__</c>) accessible from within GPU kernels.
/// </summary>
// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
[AttributeUsage(AttributeTargets.Method)]
public class GpuDeviceFunctionAttribute : Attribute;
