namespace Kassyi.GSharp.Core;

/// <summary>
/// Marks a kernel parameter, field, or property to be emitted with the <c>const</c> qualifier in the generated CUDA C++ source.
/// </summary>
/// <remarks>
/// Helps the NVIDIA CUDA compiler (<c>nvcc</c>/<c>NVRTC</c>) optimize register allocation and place constant values into read-only constant memory caches.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class GpuConstAttribute : Attribute;
