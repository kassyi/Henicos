namespace Kassyi.GSharp.Core;

/// <summary>
/// Represents CUDA block-shared memory (<c>__shared__</c>) allocation within a GPU kernel.
/// </summary>
/// <typeparam name="T">The unmanaged element type to store in shared memory.</typeparam>
/// <remarks>
/// <para>This is a compiler intrinsic placeholder intercepted by the GSharp Transpiler during code generation. It cannot be allocated or accessed directly on the host CPU.</para>
/// <para><b>Execution Target:</b> GPU kernel (<c>[GpuKernel]</c>) device code only.</para>
/// </remarks>
public readonly ref struct SharedMemory<T> where T : unmanaged
{
    /// <summary>
    /// Declares static or dynamic shared memory with the specified element capacity.
    /// </summary>
    private readonly Span<T> _span;

    /// <summary>
    /// Declares static or dynamic shared memory with the specified element capacity.
    /// </summary>
    /// <param name="size">The number of elements allocated in shared memory.</param>
    public SharedMemory(int size)
    {
        _span = ManagedGpuContext.IsActive ? ManagedGpuContext.AllocateSharedMemory<T>(size) : default;
    }

    /// <summary>
    /// Gets or sets the element at the specified zero-based index in shared memory.
    /// </summary>
    /// <param name="index">The zero-based element offset.</param>
    /// <returns>The element at the specified index.</returns>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public T this[int index]
    {
        get => ManagedGpuContext.IsActive ? _span[index] : GpuOnlyException.Throw<T>();
        set
        {
            if (ManagedGpuContext.IsActive)
            {
                _span[index] = value;
            }
            else
            {
                GpuOnlyException.Throw();
            }
        }
    }
}
