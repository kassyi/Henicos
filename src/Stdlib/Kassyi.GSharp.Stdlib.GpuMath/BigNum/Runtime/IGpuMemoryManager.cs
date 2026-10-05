namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Runtime;

/// <summary>
/// Defines the memory allocation and lifetime management contract for large-integer device buffers.
/// </summary>
/// <remarks>
/// <para>
/// Implementations allocate and manage GPU device memory or pinned host pools used for arbitrary-precision arithmetic.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> Scratch and temporary allocations return a <see cref="ScopedBigIntMemory"/> stack-bound ref struct
/// to ensure deterministic lifetime without garbage collection overhead.
/// </para>
/// </remarks>
public interface IGpuMemoryManager
{
    /// <summary>
    /// Allocates a persistent device memory handle spanning the requested number of 32-bit words.
    /// </summary>
    /// <param name="numWords">The number of 32-bit limbs to allocate.</param>
    /// <returns>A <see cref="GpuBigIntMemoryHandle"/> representing the allocated device memory segment.</returns>
    GpuBigIntMemoryHandle Allocate(int numWords);

    /// <summary>
    /// Allocates a temporary, scope-bound device memory segment that is automatically recycled or freed upon disposal.
    /// </summary>
    /// <param name="numWords">The number of 32-bit limbs required for temporary scratch space.</param>
    /// <returns>A <see cref="ScopedBigIntMemory"/> instance managing the temporary allocation lifetime.</returns>
    ScopedBigIntMemory AllocateTemp(int numWords);

    /// <summary>
    /// Releases a previously allocated device memory handle back to the pool or driver.
    /// </summary>
    /// <param name="handle">The memory handle to release.</param>
    void Free(GpuBigIntMemoryHandle handle);
}

