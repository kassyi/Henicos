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

    /// <summary>
    /// Gets or sets a value indicating whether a launch of this kernel may use an oversized shared memory configuration
    /// of the GPU (compute capability 10.7: up to 327 KB per block instead of 227 KB).
    /// </summary>
    /// <remarks>
    /// While such a launch runs, the GPU leaves the kernel only 8 KB of L1 cache. The property changes nothing on a GPU
    /// without an oversized configuration, and nothing for a launch whose <c>sharedMemBytes</c> fits the normal limit.
    /// On the CPU paths the limit is the smallest one among the GPUs of <c>GSharpTargetArchitectures</c>, so the
    /// property raises it only when every one of them has an oversized configuration.
    /// </remarks>
    public bool AllowOversizedSharedMemory
    {
        get; set;
    }

}
