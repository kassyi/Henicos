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
/// <para>Instantiating <see cref="GpuContext"/> allocates workspace memory on the active CUDA device. Ensure instances are properly disposed to prevent device memory exhaustion.</para>
/// <para><b>Thread Safety:</b> Instance members are thread-safe for allocation pooling.</para>
/// </remarks>
public class GpuContext : IDisposable
{
    /// <summary>
    /// Gets the maximum number of 32-bit limbs (words) supported in this context's fixed allocations.
    /// </summary>
    public int MaxWords
    {
        get;
    }

    /// <summary>
    /// Gets the raw native device pointer to the allocated workspace scratchpad memory (used by legacy native backend).
    /// </summary>
    public IntPtr WorkspacePointer
    {
        get;
    }

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
    public IGpuExecutionProvider Provider => Engine == GpuExecutionEngine.GSharpAot
        ? GSharpAotProvider.Instance
        : NativeProvider.Instance;

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

    /// <summary>
    /// Gets the memory manager for this context.
    /// </summary>
    public IGpuMemoryManager MemoryManager { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuContext"/> class with the specified limb capacity and engine backend.
    /// </summary>
    /// <param name="maxWords">The maximum word count for BigInteger operations.</param>
    /// <param name="engine">The GPU execution backend engine to utilize.</param>
    /// <exception cref="InvalidOperationException">Thrown when CUDA context or native workspace allocation fails.</exception>
    public GpuContext(int maxWords, GpuExecutionEngine engine = GpuExecutionEngine.GSharpAot)
    {
        MaxWords = maxWords;
        Engine = engine;
        MemoryManager = new DefaultGpuMemoryManager(Provider);

        if (Engine == GpuExecutionEngine.GSharpAot)
        {
            using var scope = new Runtime.Aot.GpuKernelLoader.CudaContextScope();
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

        if (MemoryManager is IDisposable disposableManager)
        {
            disposableManager.Dispose();
        }

        if (Engine == GpuExecutionEngine.GSharpAot)
        {
            if (Stream != IntPtr.Zero)
            {
                using var scope = new Runtime.Aot.GpuKernelLoader.CudaContextScope();
                CudaDriverApi.cuStreamDestroy(Stream);
            }
        }
        else
        {
            if (WorkspacePointer != IntPtr.Zero)
            {
                NativeMethods.GpuBigInt_Cleanup(WorkspacePointer);
            }
        }
    }

    ~GpuContext()
    {
        Dispose(false);
    }

    // Temporary shim to maintain compatibility during Phase 3 migration
    // TODO: Phase out GpuUIntBigInteger entirely from internal AST nodes
    internal GpuUIntBigInteger RentTemp(int numWords)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        var tempMem = MemoryManager.AllocateTemp(numWords);
        
        // We create a wrapper around the handle pointer for backwards compatibility
        var newTemp = new GpuUIntBigInteger(this, numWords, tempMem.Handle.Buffer.Pointer)
        {
            IsTemp = true
        };
        return newTemp;
    }

    internal void ReturnTemp(GpuUIntBigInteger temp)
    {
        if (!temp.IsTemp)
        {
            return;
        }

        if (Volatile.Read(ref _disposed) == 1)
        {
            // During disposal, MemoryManager will clean everything up
            return;
        }

        // With the new memory manager, GpuUIntBigInteger's temp memory isn't returned
        // manually via this method anymore because ScopedBigIntMemory handles it on scope exit.
        // However, for old nodes calling RentTemp, they expect ReturnTemp to free it.
        // For compatibility during transition, we call Free on the manager.
        MemoryManager.Free(new BigIntMemoryHandle(new BigIntDeviceBuffer(temp.DevicePointer, temp.NumWords), 0, temp.NumWords));
    }
}


