using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Stdlib.Math.BigInt.Providers;
using System.Collections.Concurrent;
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
    GSharpJit
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
    public IGpuExecutionProvider Provider => Engine == GpuExecutionEngine.GSharpJit
        ? GSharpJitProvider.Instance
        : NativeProvider.Instance;

    private bool _disposed;

    // Memory pooling for Lazy Evaluation
    private readonly ConcurrentDictionary<int, ConcurrentBag<GpuBigInteger>> _tempPool = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuContext"/> class with the specified limb capacity and engine backend.
    /// </summary>
    /// <param name="maxWords">The maximum word count for BigInteger operations.</param>
    /// <param name="engine">The GPU execution backend engine to utilize.</param>
    /// <exception cref="InvalidOperationException">Thrown when CUDA context or native workspace allocation fails.</exception>
    public GpuContext(int maxWords, GpuExecutionEngine engine = GpuExecutionEngine.GSharpJit)
    {
        MaxWords = maxWords;
        Engine = engine;
        if (Engine == GpuExecutionEngine.GSharpJit)
        {
            // CarryBuffer allocation logic removed for thread safety; now handled dynamically by IGpuExecutionProvider.
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
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose all pooled temporary buffers
                foreach (var bag in _tempPool.Values)
                {
                    while (bag.TryTake(out var temp))
                    {
                        // Avoid circular calls since temp.Dispose() calls back into context or disposes native
                        // Normally GpuBigInteger.Dispose calls NativeMethods.GpuBigInt_Free
                        temp.DisposeCore();
                    }
                }
                _tempPool.Clear();
            }

            if (Engine == GpuExecutionEngine.GSharpJit)
            {
                // JIT-specific cleanup
            }
            else
            {
                if (WorkspacePointer != IntPtr.Zero)
                {
                    NativeMethods.GpuBigInt_Cleanup(WorkspacePointer);
                }
            }

            _disposed = true;
        }
    }

    ~GpuContext()
    {
        Dispose(false);
    }

    internal GpuBigInteger RentTemp(int numWords)
    {
        if (_tempPool.TryGetValue(numWords, out var bag) && bag.TryTake(out var temp))
        {
            temp._disposed = false;
            temp.IsTemp = true; // ensure it's marked as temp again if needed
            return temp;
        }

        // Allocate a new one if pool is empty
        var newTemp = new GpuBigInteger(this, numWords)
        {
            IsTemp = true
        };
        return newTemp;
    }

    internal void ReturnTemp(GpuBigInteger temp)
    {
        if (!temp.IsTemp)
        {
            return;
        }

        var bag = _tempPool.GetOrAdd(temp.NumWords, _ => []);
        bag.Add(temp);
    }
}


