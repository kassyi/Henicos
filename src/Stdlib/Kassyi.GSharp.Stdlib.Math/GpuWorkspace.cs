using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.Math.BigInt.Providers;
using Kassyi.GSharp.Stdlib.Math.BigInt.Runtime;
using Kassyi.GSharp.Stdlib.Math.Interop;

namespace Kassyi.GSharp.Stdlib.Math;

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
    private readonly List<IDisposable> _trackedResources = [];

    /// <summary>
    /// Gets the memory manager for this context.
    /// </summary>
    public IGpuMemoryManager MemoryManager { get; }

    /// <summary>
    /// Registers a disposable resource to be tracked and collectively disposed by this workspace.
    /// </summary>
    public void Track(IDisposable resource)
    {
        lock (_trackedResources)
        {
            _trackedResources.Add(resource);
        }
    }

    /// <summary>
    /// Creates a nested scope. Not currently implemented for the root workspace.
    /// </summary>
    public IGpuResourceScope CreateScope() =>
        throw new NotImplementedException("Nested scopes are not yet supported on GpuWorkspace.");

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuWorkspace"/> class with the specified limb capacity and engine backend.
    /// </summary>
    /// <param name="maxWords">The maximum word count for BigInteger operations.</param>
    /// <param name="engine">The GPU execution backend engine to utilize.</param>
    /// <exception cref="InvalidOperationException">Thrown when CUDA context or native workspace allocation fails.</exception>
    public GpuWorkspace(int maxWords, GpuExecutionEngine engine = GpuExecutionEngine.GSharpAot)
    {
        MaxWords = maxWords;
        Engine = engine;

        if (Engine == GpuExecutionEngine.GSharpAot)
        {
            using var scope = new GpuKernelLoader.CudaContextScope();
            CudaDriverApi.cuStreamCreate(out var stream, 0); // CU_STREAM_DEFAULT
            Stream = stream;

            CudaDriverApi.cuCtxGetDevice(out var device);
            CudaDriverApi.cuDeviceGetAttribute(out var smCount, CudaDriverApi.CuDeviceAttribute.MultiprocessorCount,
                device);
            DeviceSmCount = smCount > 0 ? smCount : 16; // fallback if query fails
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
                    "Please ensure that the native project is built (run 'build.bat' in 'src/Native/Kassyi.GSharp.Stdlib.Math.Cuda') " +
                    "and that the CUDA Toolkit bin directory is accessible via PATH.", ex);
            }
        }

        MemoryManager = new DefaultGpuMemoryManager(Provider, Stream);
    }

    /// <summary>
    /// Releases all device memory resources, temporary pooled buffers, and workspace handles held by this context.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

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
                contextScope = new GpuKernelLoader.CudaContextScope();
            }
            catch
            {
                // Fallback: If context fails to initialize during teardown, proceed with caution.
            }
        }

        try
        {
            // Enforce safe teardown. Never let exceptions escape the finalizer thread.
            lock (_trackedResources)
            {
                foreach (var res in _trackedResources)
                {
                    try
                    {
                        res.Dispose();
                    }
                    catch
                    {
                        // Best-effort cleanup during finalization / disposal
                    }
                }

                _trackedResources.Clear();
            }

            if (MemoryManager is IDisposable disposableManager)
            {
                try
                {
                    disposableManager.Dispose();
                }
                catch
                {
                    // Best-effort
                }
            }

            if (Engine == GpuExecutionEngine.GSharpAot)
            {
                if (Stream != IntPtr.Zero)
                {
                    try
                    {
                        CudaDriverApi.cuStreamDestroy(Stream);
                    }
                    catch
                    {
                        // Best-effort
                    }
                }
            }
            else
            {
                if (WorkspacePointer != IntPtr.Zero)
                {
                    try
                    {
                        NativeMethods.GpuBigInt_Cleanup(WorkspacePointer);
                    }
                    catch
                    {
                        // Best-effort
                    }
                }
            }
        }
        finally
        {
            contextScope?.Dispose();
        }
    }

    ~GpuWorkspace()
    {
        Dispose(false);
    }

    // Temporary shim to maintain compatibility during Phase 3 migration
    // TODO: Phase out GpuUIntBigInteger entirely from internal AST nodes
    internal GpuUIntBigInteger RentTemp(int numWords)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        var tempMem = MemoryManager.AllocateTemp(numWords);
        return new GpuUIntBigInteger(this, tempMem.Handle, isTemp: true);
    }

    internal void ReturnTemp(GpuUIntBigInteger temp)
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



