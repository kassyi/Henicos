using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Constants;

namespace Kassyi.GSharp.Stdlib.GpuMath;

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
    /// Gets the dedicated CUDA Stream for this context, used for operations and CUDA Graph playback.
    /// </summary>
    /// <remarks>
    /// While GpuMath captures an internal graph (such as <c>GpuBigIntExpr.CompileToGraph</c> or wide <c>ModPow</c>),
    /// this property returns the capture's dedicated non-blocking stream to the capturing thread and always returns
    /// this workspace's own stream to other threads. The resulting graph is replayed on this workspace's stream. A
    /// buffer freed during such a capture belongs to the graph and returns to the workspace pool when the graph is
    /// disposed; other threads using the workspace meanwhile go through the shared pool. A capture started directly on
    /// this stream cannot be tracked: buffers freed during it stay parked until the workspace is disposed, and
    /// allocations during it come only from the pool.
    /// </remarks>
    public IntPtr Stream => _manager?.CurrentStream ?? _stream;

    // The workspace's own CUDA stream, ignoring any active capture session on the current thread.
    internal IntPtr BaseStream => _stream;

    /// <summary>
    /// Gets the number of streaming multiprocessors (SMs) on the active CUDA device.
    /// This value is used to clamp grid dimensions for cooperative kernel launches.
    /// </summary>
    public int DeviceSmCount
    {
        get; internal set; /*set this value only for tests*/
    }

    /// <summary>
    /// Gets the CUDA device ordinal (<c>CUdevice</c>) of this workspace, or <c>0</c> in CPU fallback mode.
    /// </summary>
    internal int DeviceOrdinal { get; }

    private readonly IntPtr _stream;
    private readonly DefaultGpuMemoryManager? _manager; // the same object as MemoryManager
    private int _disposed;
    private int _inverseSqrtDoublingRuns;
    private readonly List<IDisposable> _trackedResources = [];

    /// <summary>
    /// Gets the isolated context of this workspace (zero without one) and the number <c>CudaModuleCache</c> gives it,
    /// so that the caches keyed by that number drop its entries when the workspace destroys the context.
    /// </summary>
    internal (IntPtr Handle, long Id) IsolatedContext { get; }

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

    /// <summary>
    /// Gets the status words of <see cref="CorrectionStatus"/> as a kernel array argument, or a null array without one.
    /// </summary>
    /// <remarks>
    /// Only a CPU fallback workspace has no status words, and it launches no kernel: its operations compute on the host.
    /// The kernels therefore take the status as a non-nullable array; a null one throws where a kernel reads it.
    /// </remarks>
    internal GSharpArrayProxy CorrectionStatusProxy =>
        CorrectionStatus is { } status ? new GSharpArrayProxy(status.DevicePointer, GpuCorrectionStatus.DeviceWords) : default;

    /// <summary>
    /// Gets or sets the bit length threshold for algorithm selection in poly-algorithms like ModPow.
    /// Values smaller than the threshold will use intermediate-scale algorithms (e.g., Host-Loop),
    /// while values greater than or equal to the threshold will dispatch to giant-scale MegaKernels (e.g., NTT).
    /// Default is 1024 bits.
    /// </summary>
    public int PolyAlgorithmThreshold { get; set; } = 1024;

    /// <summary>
    /// Gets or sets the size of a square root, in 32-bit words, above which its inverse square root is computed by
    /// precision doubling driven from the host instead of one cooperative mega-kernel. Default is 5956 words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The size is the word count of the root: <c>r</c> words for an operand of <c>2r</c> words of
    /// <c>GpuBigIntDivRem.Sqrt</c>, and the significand words of a big-float square root. A root of at most this many
    /// words runs every Newton iteration at full width in one cooperative kernel; a wider root runs a chain of ordinary
    /// kernel launches whose working precision doubles from two words to the full width, with far fewer multiplied
    /// words but a few hundred more launches.
    /// </para>
    /// <para>
    /// The result is exactly the same either way; only the time differs. The size at which the two take the same time
    /// depends on the GPU (its number of SMs and its launch latency), so measure it on yours and set it here. The
    /// default, 5956 root words, is where the cooperative mega-kernel stops being the faster path: up to 5956 words
    /// its transforms fit 65536 points, and from 5957 words they need 131072 points, where host-driven precision
    /// doubling is faster. The value comes from the <c>arithmetic sqrt-path</c> benchmark on an idle machine. 0
    /// sends every root to the precision doubling, <see cref="int.MaxValue"/> every root to the mega-kernel.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int SqrtPrecisionDoublingThresholdWords
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 5956;

    /// <summary>
    /// Gets the tuning parameters of the big-float transcendental functions (<c>Exp</c>, <c>Log</c>, <c>Pow</c>) run on
    /// this workspace, on the GPU and in CPU fallback mode alike.
    /// </summary>
    public GpuBigFloatTranscendentalOptions Transcendental
    {
        get;
    } = new();

    /// <summary>
    /// Gets when the big-float <c>Divide</c> and <c>Rsqrt</c> on the GPU cut a divisor or radicand much wider than the
    /// result to its top words; the tests and the benchmarks change it to force the cut on or off.
    /// </summary>
    internal WideOperandOptions WideOperands
    {
        get;
    } = new();

    /// <summary>
    /// Gets the counts of the truncated wide-operand paths of the big-float <c>Divide</c> and <c>Rsqrt</c> on this
    /// workspace, which the tests read to see which path ran.
    /// </summary>
    internal WideOperandCounters WideOperandCounters
    {
        get;
    } = new();

    /// <summary>
    /// Gets the number of inverse square roots this workspace computed by precision doubling, which the tests read to
    /// see which path ran.
    /// </summary>
    internal int InverseSqrtDoublingRuns => Volatile.Read(ref _inverseSqrtDoublingRuns);

    /// <summary>Counts an inverse square root computed by precision doubling.</summary>
    internal void CountInverseSqrtDoubling() => Interlocked.Increment(ref _inverseSqrtDoublingRuns);

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
    /// <param name="useIsolatedContext">
    /// Whether to create and use an isolated CUDA context instead of the shared global context. Do not make a
    /// potentially unsafe call (an allocation of a number larger than every pooled buffer, a free, or an event query)
    /// while the stream of an isolated workspace captures in global or thread-local mode: in a context of
    /// <c>cuCtxCreate</c> the driver currently ends the process with an access violation instead of returning
    /// <c>CUDA_ERROR_STREAM_CAPTURE_UNSUPPORTED</c> (NVIDIA bug 7081224). Relaxed mode is not affected.
    /// </param>
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
            // Throws on a driver older than the CUDA 13.0 driver before an isolated context is created
            GpuKernelLoader.EnsureInitialized();
            var isolatedContext = IntPtr.Zero;
            if (useIsolatedContext)
            {
                CudaDriverApi.cuInit(0);
                CudaDriverApi.cuDeviceGet(out var dev, 0);
                CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out isolatedContext, 0, dev));
            }

            scope = new GpuKernelLoader.CudaContextScope(isolatedContext);
            if (useIsolatedContext)
            {
                IsolatedContext = (isolatedContext, CudaModuleCache.CurrentContextId);
            }

            CudaException.ThrowIfError(CudaDriverApi.cuStreamCreate(out var stream, 0)); // CU_STREAM_DEFAULT
            _stream = stream;

            CudaDriverApi.cuCtxGetDevice(out var device);
            DeviceOrdinal = device;
            CudaDriverApi.cuDeviceGetAttribute(out var smCount, CudaDriverApi.CuDeviceAttribute.MultiprocessorCount,
                device);
            DeviceSmCount = smCount > 0 ? smCount : 16; // fallback if query fails
        }

        try
        {
            _manager = new DefaultGpuMemoryManager(Provider, _stream);
            MemoryManager = _manager;
            if (_stream != IntPtr.Zero)
            {
                CorrectionStatus = new GpuCorrectionStatus(Provider, _stream, IsolatedContext.Handle);
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
    public IDisposable ActivateContext() => new GpuKernelLoader.CudaContextScope(IsolatedContext.Handle);

    /// <summary>
    /// Waits for the work queued on this workspace and throws the errors that its kernels reported since the previous
    /// read of the workspace stream.
    /// </summary>
    /// <remarks>
    /// Kernels report their errors without a synchronization; GpuMath throws them at the next read of the workspace
    /// stream (a copy to the host, a comparison, a bit length). Call this method to check at a point of your choice,
    /// for example after a sequence of operations whose results stay on the device. In CPU fallback mode the operations
    /// throw at the call, and this method does nothing.
    /// </remarks>
    /// <exception cref="OverflowException">
    /// A result did not fit in the buffer the caller chose, or an unsigned result was below zero (a domain error).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// An on-device correction did not converge, or the workspace stream is capturing a CUDA graph, which cannot wait.
    /// </exception>
    /// <exception cref="AggregateException">Both a domain error and a correction failure were reported.</exception>
    public void ThrowIfDeviceErrors()
    {
        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            return;
        }

        using (ActivateContext())
        {
            if (CaptureChecks.IsCapturing(this))
            {
                throw new InvalidOperationException(
                    "The workspace stream is capturing a CUDA graph, which cannot wait for the device; check " +
                    "after the graph is launched instead.");
            }

            Provider.SynchronizeStream(Stream);
        }
    }

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

        List<Exception>? exceptions = disposing ? [] : null;

        IDisposable? contextScope = null;
        if (Engine == GpuExecutionEngine.GSharpAot)
        {
            try
            {
                contextScope = new GpuKernelLoader.CudaContextScope(IsolatedContext.Handle);
            }
            catch (CudaException ex)
            {
                // The context could not be made current: still release what can be released, and report the failure
                // with the other teardown errors (dropped on the finalizer thread, which must not throw).
                exceptions?.Add(ex);
            }
        }

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

            try
            {
                // The manager releases its buffers in stream order on _stream; DisposeEngineResources waits for those
                // frees before it destroys the stream.
                // Written out here (not '?.' in a helper) so that the ownership analyzer (GSH0022) sees the call.
                if (_manager != null)
                {
                    _manager.Dispose();
                }
            }
            catch (Exception ex)
            {
                exceptions?.Add(ex);
            }

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
        if (Engine != GpuExecutionEngine.GSharpAot || _stream == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // Check if stream was abandoned in a capturing state (e.g., due to an exception)
            if (CudaDriverApi.cuStreamIsCapturing(_stream, out var captureStatus) == CUresult.CudaSuccess &&
                captureStatus != CUstreamCaptureStatus.None)
            {
                // Abort and discard uncommitted graph to prevent cross-test / cross-task contamination
                CudaDriverApi.cuStreamEndCapture(_stream, out var leakedGraph);
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

    private void DisposeEngineResources(List<Exception>? exceptions)
    {
        if (_stream == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // The memory manager freed its buffers in stream order on _stream. A stream-ordered free still pending when
            // its stream is destroyed never returns the memory to the driver (#659), so wait for this stream (only
            // this one, not the whole device) before destroying it. A failed wait is reported, and the stream is
            // still destroyed.
            CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(_stream));
            GpuKernelLoader.ThrowIfDeviceError();
        }
        catch (Exception ex)
        {
            exceptions?.Add(ex);
        }

        try
        {
            CudaDriverApi.cuStreamDestroy(_stream);

            if (IsolatedContext.Handle != IntPtr.Zero)
            {
                GSharpKernelProvider.Instance.KernelAdvancedMath.ForgetContext(IsolatedContext.Id);
                GpuKernelLoader.DestroyContext(IsolatedContext.Handle);
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

    /// <summary>
    /// Reads the allocation counters of <see cref="MemoryManager"/>, also after the workspace has been disposed.
    /// </summary>
    /// <returns>A snapshot of the counters; see <see cref="GpuMemoryStatistics"/>.</returns>
    internal GpuMemoryStatistics GetMemoryStatistics() => ((DefaultGpuMemoryManager)MemoryManager).GetStatistics();

    /// <summary>
    /// Rents a temporary buffer from the pool; the caller owns it and returns it with <c>Dispose</c>.
    /// </summary>
    /// <param name="numWords">The number of 32-bit words.</param>
    /// <returns>An owned <see cref="GpuBigUInt"/> whose contents are zero.</returns>
    /// <exception cref="ObjectDisposedException">The workspace has been disposed.</exception>
    internal GpuBigUInt RentTemp(int numWords)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        return new GpuBigUInt(this, MemoryManager.Allocate(numWords));
    }

    private uint _lastModPowNttBlockDim;

    /// <summary>
    /// Gets the block size of the most recent ModPow NTT mega-kernel launch on this workspace, which the tests read to
    /// verify the launcher's block-size rule.
    /// </summary>
    internal uint LastModPowNttBlockDim => Volatile.Read(ref _lastModPowNttBlockDim);

    /// <summary>Records the block size used to launch the ModPow NTT mega-kernel.</summary>
    internal void RecordModPowNttBlockDim(uint blockDim) => Volatile.Write(ref _lastModPowNttBlockDim, blockDim);

    private uint _lastInverseSqrtMegaBlockDim;
    private uint _lastInverseSqrtMegaGridDim;
    private uint _lastInverseSqrtMegaSharedMemBytes;
    private uint _lastReciprocalMegaBlockDim;
    private uint _lastReciprocalMegaGridDim;
    private uint _lastReciprocalMegaSharedMemBytes;

    /// <summary>
    /// Gets the launch shape of the most recent <c>InverseSqrtNewtonRaphsonMegaKernel</c> launch on this workspace,
    /// which tests read to verify block and grid dimensions.
    /// </summary>
    internal BigNum.Providers.Handlers.CooperativeMegaKernelLauncher.LaunchShape LastInverseSqrtMegaLaunchShape =>
        new(
            Volatile.Read(ref _lastInverseSqrtMegaBlockDim),
            Volatile.Read(ref _lastInverseSqrtMegaGridDim),
            Volatile.Read(ref _lastInverseSqrtMegaSharedMemBytes));

    /// <summary>Records the launch shape used to launch <c>InverseSqrtNewtonRaphsonMegaKernel</c>.</summary>
    internal void RecordInverseSqrtMegaLaunchShape(uint blockDim, uint gridDim, uint sharedMemBytes)
    {
        Volatile.Write(ref _lastInverseSqrtMegaBlockDim, blockDim);
        Volatile.Write(ref _lastInverseSqrtMegaGridDim, gridDim);
        Volatile.Write(ref _lastInverseSqrtMegaSharedMemBytes, sharedMemBytes);
    }

    /// <summary>
    /// Gets the launch shape of the most recent <c>ReciprocalNewtonRaphsonMegaKernel</c> launch on this workspace,
    /// which tests read to verify block and grid dimensions.
    /// </summary>
    internal BigNum.Providers.Handlers.CooperativeMegaKernelLauncher.LaunchShape LastReciprocalMegaLaunchShape =>
        new(
            Volatile.Read(ref _lastReciprocalMegaBlockDim),
            Volatile.Read(ref _lastReciprocalMegaGridDim),
            Volatile.Read(ref _lastReciprocalMegaSharedMemBytes));

    /// <summary>Records the launch shape used to launch <c>ReciprocalNewtonRaphsonMegaKernel</c>.</summary>
    internal void RecordReciprocalMegaLaunchShape(uint blockDim, uint gridDim, uint sharedMemBytes)
    {
        Volatile.Write(ref _lastReciprocalMegaBlockDim, blockDim);
        Volatile.Write(ref _lastReciprocalMegaGridDim, gridDim);
        Volatile.Write(ref _lastReciprocalMegaSharedMemBytes, sharedMemBytes);
    }

    private long _modPowGraphLaunches;

    /// <summary>
    /// Gets the number of CUDA graph launches executed by <c>ModPowZeroSyncController</c> on this workspace, which
    /// tests read to verify graph playback.
    /// </summary>
    internal long ModPowGraphLaunches => Volatile.Read(ref _modPowGraphLaunches);

    /// <summary>Counts a CUDA graph launch executed by <c>ModPowZeroSyncController</c>.</summary>
    internal void CountModPowGraphLaunch() => Interlocked.Increment(ref _modPowGraphLaunches);
}



