namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

/// <summary>
/// A snapshot of the allocation counters of a workspace memory manager.
/// </summary>
/// <param name="LiveBuffers">The number of buffers handed out and not yet freed.</param>
/// <param name="LiveWords">The total number of 32-bit words of the live buffers.</param>
/// <param name="PeakLiveBuffers">The largest value <paramref name="LiveBuffers"/> has reached.</param>
/// <param name="PooledBuffers">The number of freed buffers waiting in the pool for reuse.</param>
/// <param name="DriverAllocations">The number of device allocations requested from the driver since construction.</param>
/// <param name="DriverFrees">The number of buffers returned to the driver since construction.</param>
/// <remarks>
/// Each counter is read on its own, so a snapshot taken while other threads allocate or free is not atomic as a whole.
/// The counters follow the <c>{value, peak, total}</c> statistics of RMM's <c>statistics_resource_adaptor</c>: tests use
/// them to show that buffers are returned (<paramref name="LiveBuffers"/> comes back) and reused
/// (<paramref name="DriverAllocations"/> does not grow).
/// </remarks>
internal readonly record struct GpuMemoryStatistics(
    int LiveBuffers,
    long LiveWords,
    int PeakLiveBuffers,
    int PooledBuffers,
    long DriverAllocations,
    long DriverFrees);
