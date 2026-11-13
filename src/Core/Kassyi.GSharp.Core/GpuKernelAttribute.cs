namespace Kassyi.GSharp.Core;

/// <summary>
/// Marks a C# method as an entry-point GPU kernel (<c>__global__</c>) to be transpiled into CUDA C++ and compiled into GPU binaries.
/// </summary>
/// <remarks>
/// <para>Kernel methods must return <see langword="void"/> and cannot use CPU-only constructs like reference heap allocations or standard interfaces.</para>
/// </remarks>
// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
[AttributeUsage(AttributeTargets.Method)]
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
