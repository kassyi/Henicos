namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Defines low-level GPU memory allocation, host-device transfers, and direct word access methods.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread Safety:</b> Implementations must handle stream-based synchronization or ensure thread safety across concurrent stream captures.
/// </para>
/// </remarks>
public interface IGpuMemoryProvider
{
    /// <summary>
    /// Allocates unmanaged device global memory for the specified number of 32-bit words.
    /// </summary>
    /// <param name="numWords">The number of 32-bit words (limbs) to allocate.</param>
    /// <returns>An unmanaged pointer (<c>CUdeviceptr</c>) to the allocated device memory segment.</returns>
    IntPtr AllocateMemory(int numWords);

    /// <summary>
    /// Releases previously allocated device global memory.
    /// </summary>
    /// <param name="devicePointer">The unmanaged pointer to the device memory segment to free.</param>
    /// <param name="stream">
    /// The stream that orders the release, or <see cref="IntPtr.Zero"/> for an immediate release.
    /// <list type="bullet">
    ///   <item><description>
    ///   <see cref="IntPtr.Zero"/>: freed immediately. On CUDA this waits for all work on every stream of the device,
    ///   so it is safe even when the memory was used on streams the caller does not know.
    ///   </description></item>
    ///   <item><description>
    ///   A stream: freed once <paramref name="stream"/> reaches this point, without blocking the calling thread.
    ///   The caller guarantees that all work using the memory is ordered on <paramref name="stream"/>,
    ///   and that the stream is not being captured into a CUDA Graph.
    ///   </description></item>
    /// </list>
    /// </param>
    void FreeMemory(IntPtr devicePointer, IntPtr stream = 0);

    /// <summary>
    /// Asynchronously clears the specified device memory region to zero on the given CUDA stream.
    /// </summary>
    /// <param name="devicePointer">The unmanaged pointer to the starting device memory address.</param>
    /// <param name="numWords">The number of 32-bit words to clear.</param>
    /// <param name="stream">The unmanaged CUDA stream handle.</param>
    void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream);

    /// <summary>
    /// Copies limb data synchronously or asynchronously from host memory to device global memory.
    /// </summary>
    /// <param name="devicePointer">The destination device memory pointer.</param>
    /// <param name="hostData">The source managed 32-bit unsigned integer array.</param>
    /// <param name="numWords">The number of words to copy.</param>
    /// <param name="stream">The optional CUDA stream handle for asynchronous copy operations.</param>
    void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0);

    /// <summary>
    /// Copies limb data from device global memory to host memory and waits until the data has arrived.
    /// </summary>
    /// <param name="hostData">The destination managed 32-bit unsigned integer array.</param>
    /// <param name="devicePointer">The source device memory pointer.</param>
    /// <param name="numWords">The number of words to copy.</param>
    /// <param name="stream">
    /// The stream the copy is ordered on; the copy observes all work enqueued on it before the call.
    /// </param>
    /// <remarks>
    /// Synchronous: <paramref name="hostData"/> holds the data when the method returns, whether the array is pageable or
    /// page-locked, so callers need no additional stream synchronization. Use <see cref="CopyDeviceToHostAsync"/> to
    /// avoid blocking the calling thread while queued work completes.
    /// </remarks>
    void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0);

    /// <summary>
    /// Asynchronously copies limb data from device global memory to host memory without blocking the calling thread.
    /// </summary>
    /// <param name="hostData">The destination host memory, at least <paramref name="numWords"/> words long.</param>
    /// <param name="devicePointer">The source device memory pointer.</param>
    /// <param name="numWords">The number of words to copy.</param>
    /// <param name="stream">
    /// The stream the copy is ordered on; the copy observes all work enqueued on it before the call.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that detaches the awaiter. Device work is not canceled, and <paramref name="hostData"/> is not written
    /// after cancellation.
    /// </param>
    /// <returns>A <see cref="ValueTask"/> that completes once <paramref name="hostData"/> holds the data.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="numWords"/> exceeds the length of <paramref name="hostData"/>.
    /// </exception>
    ValueTask CopyDeviceToHostAsync(Memory<uint> hostData, IntPtr devicePointer, int numWords, IntPtr stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies data directly between two device memory allocations.
    /// </summary>
    /// <param name="destPointer">The destination device memory pointer.</param>
    /// <param name="srcPointer">The source device memory pointer.</param>
    /// <param name="numWords">The number of 32-bit words to copy.</param>
    /// <param name="stream">The optional CUDA stream handle for asynchronous copy operations.</param>
    void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0);

    /// <summary>
    /// Reads a single 32-bit limb value directly from device memory at the given word offset.
    /// </summary>
    /// <param name="devicePointer">The base device memory pointer.</param>
    /// <param name="wordOffset">The zero-based 32-bit word offset.</param>
    /// <param name="stream">
    /// The stream the read is ordered on, or <see cref="IntPtr.Zero"/> for the legacy null stream.
    /// A stream observes exactly the work enqueued on it and does not wait for unrelated streams.
    /// The null stream additionally waits for all work on every blocking stream of the context.
    /// </param>
    /// <returns>The 32-bit unsigned integer read from the device.</returns>
    /// <remarks>Synchronous: the value is available when the method returns.</remarks>
    uint ReadWord(IntPtr devicePointer, int wordOffset = 0, IntPtr stream = 0);

    /// <summary>
    /// Writes a single 32-bit limb value directly to device memory at the given word offset.
    /// </summary>
    /// <param name="devicePointer">The base device memory pointer.</param>
    /// <param name="wordOffset">The zero-based 32-bit word offset.</param>
    /// <param name="value">The 32-bit unsigned integer value to write.</param>
    /// <param name="stream">
    /// The stream the write is ordered on, or <see cref="IntPtr.Zero"/> for the legacy null stream.
    /// </param>
    /// <remarks>
    /// With a stream the write is only enqueued and the host is not blocked; it is visible to later work enqueued on the
    /// same stream. Without a stream the write completes before the method returns.
    /// </remarks>
    void WriteWord(IntPtr devicePointer, int wordOffset, uint value, IntPtr stream = 0);

    /// <summary>
    /// Blocks the host thread until all previously submitted tasks in the specified CUDA stream have completed.
    /// </summary>
    /// <param name="stream">The unmanaged stream handle to synchronize.</param>
    void SynchronizeStream(IntPtr stream);

    /// <summary>
    /// Asynchronously waits until all previously submitted work in the specified CUDA stream has completed, without
    /// blocking the calling thread.
    /// </summary>
    /// <param name="stream">
    /// The unmanaged stream handle to wait for. <see cref="IntPtr.Zero"/> completes immediately, like
    /// <see cref="SynchronizeStream"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that detaches the awaiter; the stream keeps executing and remains usable.
    /// </param>
    /// <returns>A <see cref="ValueTask"/> that completes when the stream has drained.</returns>
    ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default);
}
