using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Runtime;

/// <summary>
/// Provides extension methods for <see cref="CudaStream"/> inter-stream dependency management.
/// </summary>
public static class CudaStreamExtensions
{
    // CU_EVENT_DISABLE_TIMING (0x02): disables timing data collection for optimal cuStreamWaitEvent performance.
    private const uint EventDisableTimingFlag = 0x02;

    /// <summary>
    /// Records an event on <paramref name="producer"/> and makes <paramref name="consumer"/> wait on the device
    /// until all work currently enqueued on <paramref name="producer"/> has completed.
    /// </summary>
    /// <param name="consumer">The stream that will wait before executing subsequently enqueued work.</param>
    /// <param name="producer">The stream whose already-enqueued work must complete first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="consumer"/> or <paramref name="producer"/> is <see langword="null"/>.</exception>
    /// <exception cref="CudaException">The driver failed to create, record, or wait on the synchronization event.</exception>
    public static void WaitFor(this CudaStream consumer, CudaStream producer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ArgumentNullException.ThrowIfNull(producer);

        if (ReferenceEquals(consumer, producer) || consumer.Handle == producer.Handle)
        {
            return;
        }

        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuStreamGetCtx(producer.Handle, out var producerContext));
        using var producerScope = new GpuKernelLoader.CudaContextScope(producerContext);

        CudaException.ThrowIfError(CudaDriverApi.cuEventCreate(out var eventHandle, EventDisableTimingFlag));
        try
        {
            CudaException.ThrowIfError(CudaDriverApi.cuEventRecord(eventHandle, producer.Handle));
            CudaException.ThrowIfError(CudaDriverApi.cuStreamWaitEvent(consumer.Handle, eventHandle, 0));
        }
        finally
        {
            // The CUDA driver defers internal event destruction until all streams waiting on the event have reached it.
            CudaDriverApi.cuEventDestroy_v2(eventHandle);
        }
    }
}
