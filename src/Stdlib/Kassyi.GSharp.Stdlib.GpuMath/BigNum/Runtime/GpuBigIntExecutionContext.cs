namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Runtime;

using System;

/// <summary>
/// Encapsulates the execution state, memory manager, arithmetic provider, and CUDA stream for a GPU BigNum operation.
/// </summary>
/// <param name="gpuWorkspace">The GPU workspace managing global configuration and device allocation.</param>
/// <param name="memoryManager">The memory manager handling device buffers and temporary scratch spaces.</param>
/// <param name="arithmetic">The arithmetic provider handling low-level kernel launches.</param>
/// <param name="stream">The unmanaged CUDA stream (<c>cudaStream_t</c>) handle used for asynchronous operations.</param>
/// <remarks>
/// <para>
/// <b>Zero-Allocation:</b> As a <see langword="ref struct"/>, this context lives solely on the stack and incurs no garbage collection overhead.
/// </para>
/// </remarks>
public readonly ref struct GpuBigIntExecutionContext(
    GpuWorkspace gpuWorkspace,
    IGpuMemoryManager memoryManager,
    IGpuBigIntArithmetic arithmetic,
    IntPtr stream)
{
    /// <summary>
    /// Gets the parent GPU workspace.
    /// </summary>
    public GpuWorkspace GpuWorkspace { get; } = gpuWorkspace;

    /// <summary>
    /// Gets the memory manager responsible for device buffer allocations.
    /// </summary>
    public IGpuMemoryManager MemoryManager { get; } = memoryManager;

    /// <summary>
    /// Gets the low-level arithmetic operations provider.
    /// </summary>
    public IGpuBigIntArithmetic Arithmetic { get; } = arithmetic;

    /// <summary>
    /// Gets the CUDA stream handle associated with this execution pipeline.
    /// </summary>
    public IntPtr Stream { get; } = stream;

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigIntExecutionContext"/> struct using a legacy adapter fallback for arithmetic operations.
    /// </summary>
    /// <param name="gpuWorkspace">The GPU workspace.</param>
    /// <param name="memoryManager">The memory manager.</param>
    /// <param name="stream">The CUDA stream handle.</param>
    public GpuBigIntExecutionContext(GpuWorkspace gpuWorkspace, IGpuMemoryManager memoryManager, IntPtr stream)
        : this(gpuWorkspace, memoryManager, default(LegacyProviderArithmeticAdapter), stream)
    {
    }
}

