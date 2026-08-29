namespace Kassyi.GSharp.Core;

/// <summary>
/// Indicates that a kernel parameter value should be injected as a compile-time <c>#define</c> constant in the CUDA C++ source rather than passed via runtime kernel parameter buffers.
/// </summary>
/// <remarks>
/// <para>Enables aggressive compiler optimizations such as compile-time loop unrolling (<c>#pragma unroll</c>), dead-code elimination, and register pressure reduction.</para>
/// </remarks>
/// <remarks><para>Implements: <b>GEN-37, GEN-41</b></para></remarks>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class JitConstantAttribute : Attribute;
