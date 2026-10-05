using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Runtime;
using Kassyi.GSharp.Stdlib.GpuMath.Interop;

namespace Kassyi.GSharp.Stdlib.GpuMath;

/// <summary>
/// Specifies the GPU kernel execution engine backend.
/// </summary>
public enum GpuExecutionEngine
{
    /// <summary>Executes kernels via the legacy pre-compiled native CUDA C++ dynamic library.</summary>
    LegacyNative,

    /// <summary>Executes kernels via the GSharp JIT transpilation and NVRTC runtime compiler.</summary>
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
    public int MaxWords { get; }

    /// <summary>
    /// Gets the raw native device pointer to the allocated workspace scratchpad memory (used by legacy native backend).
    /// </summary>
    public IntPtr WorkspacePointer { get; }

    /// <summary>
    /// Gets the active GPU execution engine type.
    /// </summary>
    public GpuExecutionEngine Engine { get; }

    /// <summary>
    /// Gets the active GPU arithmetic execution provider implementation.
    /// </summary>
    public IGpuExecutionProvider Provider => Engine == GpuExecutionEngine.GSharpAot
        ? GSharpAotProvider.Instance
        : NativeProvider.Instance;

    /// <summary>
    /// Gets the dedicated CUDA Stream for this context, used for operations and CUDA Graph capture.
    /// </summary>
    public IntPtr Stream { get; }

    /// <summary>
    /// Gets the number of streaming multiprocessors (SMs) on the active CUDA device.
    /// This value is used to clamp grid dimensions for cooperative kernel launches.
    /// </summary>
    public int DeviceSmCount { get; internal set; /*set this value only for tests*/ }

    private int _disposed;
    private readonly IntPtr _isolatedContext;
    private readonly List<IDisposable> _trackedResources = [];

    /// <summary>
    /// Gets the memory manager for this context.
    /// </summary>
    public IGpuMemoryManager MemoryManager { get; }

    /// <summary>
    /// Gets or sets the bit length threshold for algorithm selection in poly-algorithms like ModPow.
    /// Values smaller than the threshold will use intermediate-scale algorithms (e.g., Host-Loop or RNS),
    /// while values greater than or equal to the threshold will dispatch to giant-scale MegaKernels (e.g., NTT).
    /// Default is 1024 bits.
    /// </summary>
    public int PolyAlgorithmThreshold { get; set; } = 1024;

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
    public IGpuResourceScope CreateScope() =>
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

        GpuKernelLoader.CudaContextScope? scope = null;
        if (Engine == GpuExecutionEngine.GSharpAot)
        {
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
                CudaDriverApi.cuStreamCreate(out var stream, 0); // CU_STREAM_DEFAULT
                Stream = stream;

                CudaDriverApi.cuCtxGetDevice(out var device);
                CudaDriverApi.cuDeviceGetAttribute(out var smCount, CudaDriverApi.CuDeviceAttribute.MultiprocessorCount,
                    device);
                DeviceSmCount = smCount > 0 ? smCount : 16; // fallback if query fails
            }
        }
        else
        {
            try
            {
                WorkspacePointer = NativeMethods.GpuBigInt_Init(maxWords);
                if (WorkspacePointer == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to initialize GPU context.");
                }
            }
            catch (DllNotFoundException ex)
            {
                throw new InvalidOperationException(
                    "The native CUDA DLL 'Kassyi.GSharp.GpuBigInt.Cuda' or one of its dependencies could not be loaded. " +
                    "Please ensure that the native project is built (run 'build.bat' in 'src/Native/Kassyi.GSharp.Stdlib.GpuMath.Cuda') " +
                    "and that the CUDA Toolkit bin directory is accessible via PATH.", ex);
            }
        }

        try
        {
            MemoryManager = new DefaultGpuMemoryManager(Provider, Stream);
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
            DisposeTrackedResources(exceptions);
            DisposeMemoryManager(exceptions);
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
            disposableManager.Dispose();
        }
        catch (Exception ex)
        {
            exceptions?.Add(ex);
        }
    }

    private void DisposeEngineResources(List<Exception>? exceptions)
    {
        if (Engine == GpuExecutionEngine.GSharpAot)
        {
            if (Stream == IntPtr.Zero)
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

                CudaDriverApi.cuStreamDestroy(Stream);

                if (_isolatedContext != IntPtr.Zero)
                {
                    CudaDriverApi.cuCtxDestroy_v2(_isolatedContext);
                }
            }
            catch (Exception ex)
            {
                exceptions?.Add(ex);
            }
        }
        else
        {
            if (WorkspacePointer == IntPtr.Zero)
            {
                return;
            }

            try
            {
                NativeMethods.GpuBigInt_Cleanup(WorkspacePointer);
            }
            catch (Exception ex)
            {
                exceptions?.Add(ex);
            }
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



