using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Constants;

namespace Kassyi.GSharp.Stdlib.GpuMath;

/// <summary>
/// Specifies the GPU kernel execution engine backend.
/// </summary>
public enum GpuExecutionEngine
{
    /// <summary>Executes pre-compiled GSharp device kernels via Ahead-Of-Time (AOT) loaded modules.</summary>
    GSharpAot
}

/// <summary>
/// Represents a GPU execution context managing memory allocation pools, device workspaces, and backend providers for large integer computations.
/// </summary>
/// <remarks>
/// <para>Instantiating <see cref="GpuWorkspace"/> allocates workspace memory on the active CUDA device. Ensure instances are properly disposed to prevent device memory exhaustion.</para>
/// <para><b>Thread Safety:</b> Instance members are thread-safe for allocation pooling.</para>
/// </remarks>
public class GpuWorkspace : IGpuResourceScope
{
    /// <summary>
    /// Gets the maximum number of 32-bit limbs (words) supported in this context's fixed allocations.
    /// </summary>
    public int MaxWords
    {
        get;
    }

    /// <summary>
    /// Gets the raw native device pointer to the allocated workspace scratchpad memory.
    /// </summary>
    [Obsolete("Legacy native workspace pointers are deprecated and always IntPtr.Zero.")]
    public IntPtr WorkspacePointer => IntPtr.Zero;

    /// <summary>
    /// Gets the active GPU execution engine type.
    /// </summary>
    public GpuExecutionEngine Engine
    {
        get;
    }

    /// <summary>
    /// Gets the active GPU arithmetic execution provider implementation.
    /// </summary>
    public IGpuExecutionProvider Provider => GSharpKernelProvider.Instance;

    /// <summary>
    /// Gets the dedicated CUDA Stream for this context, used for operations and CUDA Graph capture.
    /// </summary>
    public IntPtr Stream
    {
        get;
    }

    /// <summary>
    /// Gets the number of streaming multiprocessors (SMs) on the active CUDA device.
    /// This value is used to clamp grid dimensions for cooperative kernel launches.
    /// </summary>
    public int DeviceSmCount
    {
        get; internal set; /*set this value only for tests*/
    }

    private int _disposed;
    private readonly IntPtr _isolatedContext;
    private readonly List<IDisposable> _trackedResources = [];

    /// <summary>
    /// Gets the memory manager for this context.
    /// </summary>
    public IGpuMemoryManager MemoryManager
    {
        get;
    }

    /// <summary>
    /// Gets the device words through which unconverged on-device corrections are reported, or <see langword="null"/> in
    /// CPU fallback mode.
    /// </summary>
    internal GpuCorrectionStatus? CorrectionStatus
    {
        get;
    }

    /// <summary>Gets the device address of <see cref="CorrectionStatus"/>, or <see cref="IntPtr.Zero"/> without one.</summary>
    internal IntPtr CorrectionStatusPointer => CorrectionStatus?.DevicePointer ?? IntPtr.Zero;

    /// <summary>
    /// Gets or sets the bit length threshold for algorithm selection in poly-algorithms like ModPow.
    /// Values smaller than the threshold will use intermediate-scale algorithms (e.g., Host-Loop or RNS),
    /// while values greater than or equal to the threshold will dispatch to giant-scale MegaKernels (e.g., NTT).
    /// Default is 1024 bits.
    /// </summary>
    public int PolyAlgorithmThreshold { get; set; } = 1024;

    /// <summary>
    /// Gets the device copies of the constants that the big-float transcendental functions share (ln 2), uploaded on
    /// first use and released with the workspace.
    /// </summary>
    internal GpuBigFloatConstantCache Constants
    {
        get;
    }

    /// <summary>
    /// Registers a disposable resource to be tracked and deterministically disposed upon workspace disposal.
    /// </summary>
    /// <param name="resource">The disposable resource to track.</param>
    public void Track(IDisposable resource)
    {
        lock (_trackedResources)
        {
            _trackedResources.Add(resource);
        }
    }

    /// <summary>
    /// Creates a nested GPU resource scope. Not currently supported on the root workspace.
    /// </summary>
    /// <returns>A new <see cref="IGpuResourceScope"/> instance.</returns>
    /// <exception cref="NotImplementedException">Thrown unconditionally as nested scopes are not yet supported on the root workspace.</exception>
    [ExcludeFromCodeCoverage] public IGpuResourceScope CreateScope() =>
        throw new NotImplementedException("Nested scopes are not yet supported on GpuWorkspace.");

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuWorkspace"/> class with the specified limb capacity and engine backend.
    /// </summary>
    /// <param name="maxWords">The maximum word count for BigInteger operations.</param>
    /// <param name="engine">The GPU execution backend engine to utilize.</param>
    /// <param name="useIsolatedContext">Whether to create and use an isolated CUDA context instead of the shared global context.</param>
    /// <exception cref="InvalidOperationException">Thrown when CUDA context or native workspace allocation fails.</exception>
    public GpuWorkspace(int maxWords, GpuExecutionEngine engine = GpuExecutionEngine.GSharpAot,
        bool useIsolatedContext = false)
    {
        MaxWords = maxWords;
        Engine = engine;

        Constants = new GpuBigFloatConstantCache(this);

        GpuKernelLoader.CudaContextScope? scope = null;
        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            DeviceSmCount = 1;
        }
        else
        {
            if (useIsolatedContext)
            {
                CudaDriverApi.cuInit(0);
                CudaDriverApi.cuDeviceGet(out var dev, 0);
                CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out _isolatedContext, 0, dev));
            }

            scope = new GpuKernelLoader.CudaContextScope(_isolatedContext);
            CudaException.ThrowIfError(CudaDriverApi.cuStreamCreate(out var stream, 0)); // CU_STREAM_DEFAULT
            Stream = stream;

            CudaDriverApi.cuCtxGetDevice(out var device);
            CudaDriverApi.cuDeviceGetAttribute(out var smCount, CudaDriverApi.CuDeviceAttribute.MultiprocessorCount,
                device);
            DeviceSmCount = smCount > 0 ? smCount : 16; // fallback if query fails
        }

        try
        {
            MemoryManager = new DefaultGpuMemoryManager(Provider, Stream);
            if (Stream != IntPtr.Zero)
            {
                CorrectionStatus = new GpuCorrectionStatus(Provider, Stream, _isolatedContext);
            }
        }
        finally
        {
            scope?.Dispose();
        }
    }

    /// <summary>
    /// Activates the CUDA context associated with this workspace for the lifetime of the returned scope.
    /// </summary>
    /// <returns>An <see cref="IDisposable"/> token restoring the previous CUDA context upon disposal.</returns>
    public IDisposable ActivateContext() => new GpuKernelLoader.CudaContextScope(_isolatedContext);

    /// <summary>
    /// Releases all device memory resources, temporary pooled buffers, and workspace handles held by this context.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="GpuWorkspace"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        IDisposable? contextScope = null;
        if (Engine == GpuExecutionEngine.GSharpAot)
        {
            try
            {
                contextScope = new GpuKernelLoader.CudaContextScope(_isolatedContext);
            }
            catch
            {
                // Fallback: If context fails to initialize during teardown, proceed with caution.
            }
        }

        List<Exception>? exceptions = disposing ? [] : null;

        try
        {
            // Must precede every release: stream-ordered frees on a capturing stream would become nodes of the
            // discarded graph instead of releasing memory.
            AbortLeakedCapture(exceptions);
            DisposeTrackedResources(exceptions);
            try
            {
                // Before the memory manager, which owns the buffers of the constants.
                Constants.Dispose();
            }
            catch (Exception ex)
            {
                exceptions?.Add(ex);
            }

            DisposeMemoryManager(exceptions);
            try
            {
                // Stream-ordered release of the device words; the page-locked slot goes back to the process-wide pool
                // once no read copies into it.
                // Written out here (not '?.' in a helper) so that the ownership analyzer (GSH0022) sees the call.
                if (CorrectionStatus != null)
                {
                    CorrectionStatus.Dispose();
                }
            }
            catch (Exception ex)
            {
                exceptions?.Add(ex);
            }

            DisposeEngineResources(exceptions);
        }
        finally
        {
            try
            {
                contextScope?.Dispose();
            }
            catch (Exception ex)
            {
                exceptions?.Add(ex);
            }
        }

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException("One or more errors occurred while disposing GpuWorkspace.", exceptions);
        }
    }

    private void AbortLeakedCapture(List<Exception>? exceptions)
    {
        if (Engine != GpuExecutionEngine.GSharpAot || Stream == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // Check if stream was abandoned in a capturing state (e.g., due to an exception)
            if (CudaDriverApi.cuStreamIsCapturing(Stream, out var captureStatus) == CUresult.CudaSuccess &&
                captureStatus != CUstreamCaptureStatus.None)
            {
                // Abort and discard uncommitted graph to prevent cross-test / cross-task contamination
                CudaDriverApi.cuStreamEndCapture(Stream, out var leakedGraph);
                if (leakedGraph != IntPtr.Zero)
                {
                    CudaDriverApi.cuGraphDestroy(leakedGraph);
                }
            }
        }
        catch (Exception ex)
        {
            exceptions?.Add(ex);
        }
    }

    private void DisposeTrackedResources(List<Exception>? exceptions)
    {
        // Enforce safe teardown. Never let unhandled exceptions escape the finalizer thread.
        lock (_trackedResources)
        {
            foreach (var res in _trackedResources)
            {
                try
                {
                    res.Dispose();
                }
                catch (Exception ex)
                {
                    exceptions?.Add(ex);
                }
            }

            _trackedResources.Clear();
        }
    }

    private void DisposeMemoryManager(List<Exception>? exceptions)
    {
        if (MemoryManager is not IDisposable disposableManager)
        {
            return;
        }

        try
        {
            // No host-side synchronization: the manager releases its buffers in stream order on Stream.
            disposableManager.Dispose();
        }
        catch (Exception ex)
        {
            exceptions?.Add(ex);
        }
    }

    private void DisposeEngineResources(List<Exception>? exceptions)
    {
        if (Stream == IntPtr.Zero)
        {
            return;
        }

        try
        {
            CudaDriverApi.cuStreamDestroy(Stream);

            if (_isolatedContext != IntPtr.Zero)
            {
                GpuKernelLoader.DestroyContext(_isolatedContext);
            }
        }
        catch (Exception ex)
        {
            exceptions?.Add(ex);
        }
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="GpuWorkspace"/> class.
    /// </summary>
    ~GpuWorkspace()
    {
        Dispose(false);
    }

    internal GpuBigUInt RentTemp(int numWords)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        var tempMem = MemoryManager.AllocateTemp(numWords);
        return new GpuBigUInt(this, tempMem.Handle, isTemp: true);
    }

    internal void ReturnTemp(GpuBigUInt temp)
    {
        if (!temp.IsTemp)
        {
            return;
        }

        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        MemoryManager.Free(temp.Handle);
    }
}



