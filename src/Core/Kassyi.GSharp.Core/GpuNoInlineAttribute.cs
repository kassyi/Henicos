namespace Kassyi.GSharp.Core;

/// <summary>
/// Prevents inlining (<c>__noinline__</c>) of the annotated device function in the transpiled CUDA C++ code.
/// </summary>
// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuNoInlineAttribute : Attribute;
