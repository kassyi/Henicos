namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Runtime;

using System;
using System.Threading;

/// <summary>
/// Encapsulates a contiguous block of allocated device memory words for GPU arithmetic operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Serves as an internal tracking token and lifetime container for unmanaged GPU memory pointers.
/// Deallocation is handled cooperatively with the parent memory manager or pool.
/// </para>
/// </remarks>
internal sealed class GpuBigIntDeviceBuffer(IntPtr pointer, int totalWords) : IDisposable
{
    private int _disposed;

    /// <summary>
    /// Gets the raw unmanaged device memory pointer.
    /// </summary>
    public IntPtr Pointer { get; } = pointer;

    /// <summary>
    /// Gets the total capacity of the device buffer in 32-bit words (limbs).
    /// </summary>
    public int TotalWords { get; } = totalWords;

    /// <summary>
    /// Marks the buffer instance as disposed.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            // Note: Actual device memory deallocation (cuMemFree) is managed 
            // by the provider/manager that created this buffer.
        }
    }
}

