namespace Kassyi.GSharp.Core;

/// <summary>
/// Indicates that a kernel parameter should be injected as a #define constant 
/// into the CUDA C++ source at JIT compilation time, rather than passed as a runtime argument.
/// This allows aggressive optimizations like loop unrolling (#pragma unroll) and register reduction.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class JitConstantAttribute : Attribute;
