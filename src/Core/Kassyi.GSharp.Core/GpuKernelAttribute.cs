namespace Kassyi.GSharp.Core;

/// <summary>
/// Marks a C# method as an entry-point GPU kernel (<c>__global__</c>) to be transpiled into CUDA C++ and compiled at JIT/runtime.
/// </summary>
/// <remarks>
/// <para>Kernel methods must return <see langword="void"/> and cannot use CPU-only constructs like reference heap allocations or standard interfaces.</para>
/// </remarks>
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[AttributeUsage(AttributeTargets.Method)]
/// <remarks><para>Implements: <b>GEN-38</b></para></remarks>
public class GpuKernelAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether this kernel is launched as a cooperative kernel (<c>cuLaunchCooperativeKernel</c> / CUDA Cooperative Groups).
    /// </summary>
    public bool Cooperative
    {
        get; set;
    }
}

/// <summary>
/// Marks a C# method as a callable GPU device function (<c>__device__</c>) accessible from within GPU kernels.
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[AttributeUsage(AttributeTargets.Method)]
public class GpuDeviceFunctionAttribute : Attribute;

/// <summary>
/// Requests force-inlining (<c>__forceinline__</c>) of the annotated device function in the transpiled CUDA C++ code.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuInlineAttribute : Attribute;

/// <summary>
/// Prevents inlining (<c>__noinline__</c>) of the annotated device function in the transpiled CUDA C++ code.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuNoInlineAttribute : Attribute;
