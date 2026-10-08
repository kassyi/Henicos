using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Runtime JIT/AOT compilation engine that manages thread contexts and loads executable GPU modules.
/// </summary>
// ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
public sealed class GpuKernelLoader
{
    private static volatile bool s_isGlobalInit;
    private static IntPtr s_globalContext;
    private static readonly Lock s_initLock = new();
    private static volatile bool s_isCpuFallbackMode;

    /// <summary>
    /// Gets or sets a value indicating whether to forcibly emulate GPU runtime on CPU host memory.
    /// </summary>
    public static bool ForceCpuFallback
    {
        get; set;
    }

    /// <summary>
    /// Gets a value indicating whether the runtime is operating in CPU host fallback mode.
    /// </summary>
    public static bool IsCpuFallbackMode => ForceCpuFallback || s_isCpuFallbackMode;

    /// <summary>
    /// Scoped RAII structure that activates a CUDA context on the current thread and restores the previous context upon disposal.
    /// </summary>
    public readonly struct CudaContextScope : IDisposable
    {
        [ThreadStatic]
        private static IntPtr s_tActiveContext;

        private readonly IntPtr _prevContext;
        private readonly IntPtr _targetContext;
        private readonly IntPtr _prevActiveContext;
        private readonly bool _isFallback;

        /// <summary>
        /// Initializes a new instance of the <see cref="CudaContextScope"/> struct using the global primary context.
        /// </summary>
        public CudaContextScope() : this(IntPtr.Zero)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CudaContextScope"/> struct with a custom CUDA context.
        /// </summary>
        /// <param name="customContext">The explicit CUDA context handle to activate.</param>
        public CudaContextScope(IntPtr customContext)
        {
            EnsureInitialized();
            if (IsCpuFallbackMode)
            {
                _isFallback = true;
                _prevContext = IntPtr.Zero;
                _targetContext = IntPtr.Zero;
                _prevActiveContext = IntPtr.Zero;
                return;
            }

            _isFallback = false;
            _prevActiveContext = s_tActiveContext;

            _targetContext = customContext != IntPtr.Zero
                ? customContext
                : (s_tActiveContext != IntPtr.Zero ? s_tActiveContext : s_globalContext);

            s_tActiveContext = _targetContext;

            CudaDriverApi.cuCtxGetCurrent(out _prevContext);
            if (_prevContext != _targetContext)
            {
                CudaException.ThrowIfError(CudaDriverApi.cuCtxSetCurrent(_targetContext));
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_isFallback)
            {
                return;
            }

            s_tActiveContext = _prevActiveContext;

            if (_prevContext != _targetContext)
            {
                CudaDriverApi.cuCtxSetCurrent(_prevContext);
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the CUDA driver is available and initialized.
    /// </summary>
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static bool IsDriverAvailable
    {
        get
        {
            try
            {
                EnsureInitialized();
                return !s_isCpuFallbackMode;
            }
            catch (DllNotFoundException)
            {
                // Expected when CUDA is not installed; handled gracefully by the fallback logic.
                return false;
            }
            catch (CudaException)
            {
                // Expected when CUDA is installed but driver is faulty/unavailable.
                return false;
            }
        }
    }

    private static void EnsureInitialized()
    {
        // ReSharper disable once InconsistentlySynchronizedField -- Double-checked locking pattern
        if (s_isGlobalInit)
        {
            return;
        }

        lock (s_initLock)
        {
            if (s_isGlobalInit)
            {
                return;
            }

            if (ForceCpuFallback)
            {
                s_isCpuFallbackMode = true;
                s_isGlobalInit = true;
                return;
            }

            try
            {
                CudaException.ThrowIfError(CudaDriverApi.cuInit(0));
                CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out var device, 0));

                CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out s_globalContext, 0, device));
                s_isCpuFallbackMode = false;
            }
            catch (DllNotFoundException)
            {
                s_isCpuFallbackMode = true;
            }
            catch (CudaException)
            {
                s_isCpuFallbackMode = true;
            }

            s_isGlobalInit = true;
        }
    }

    private readonly struct GpuModule(IntPtr module, IntPtr function)
    {
        public IntPtr Module { get; } = module;
        public IntPtr Function { get; } = function;
    }



    // AOT Cache uses Hash256 and KernelName to prevent collisions and avoid allocations
    private static readonly ConcurrentDictionary<(AotSignatureValidator.Hash256 Hash, string KernelName), Lazy<GpuModule>> s_functionCache =
        new();

#pragma warning disable IDE0051 // Justification: Public API for library
    /// <summary>
    /// Unloads all cached GPU modules and clears the module cache.
    /// </summary>
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static void Shutdown()
    {
        if (!s_isGlobalInit)
        {
            s_functionCache.Clear();
            return;
        }

        try
        {
            using var scope = new CudaContextScope();

            foreach (var lazyModule in s_functionCache.Values)
            {
                if (!lazyModule.IsValueCreated)
                {
                    continue;
                }

                var module = lazyModule.Value.Module;
                try
                {
                    CudaDriverApi.cuModuleUnload(module);
                }
                catch
                {
                    // Ignore cleanup errors on best-effort basis during Shutdown
                }
            }
        }
        catch
        {
            // Ignore context scope initialization errors during shutdown
        }
        finally
        {
            s_functionCache.Clear();
        }
    }

    /// <summary>
    /// Blocks until the device has completed all preceding requested tasks.
    /// </summary>
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static void SynchronizeDevice()
    {
        using var scope = new CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());
    }

    /// <summary>
    /// Loads an Ahead-Of-Time (AOT) compiled Fatbin directly into the CUDA driver.
    /// </summary>
    /// <param name="aotFatbin">The raw byte span of the AOT Fatbin binary.</param>
    /// <param name="kernelName">The name of the kernel entry point to load.</param>
    /// <returns>A native handle (<see cref="IntPtr"/>) to the loaded CUDA kernel function (<c>CUfunction</c>).</returns>
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static IntPtr LoadAotKernel(ReadOnlySpan<byte> aotFatbin, string kernelName)
    {
        // 1. Fast path: Extract embedded hash struct from signature to check cache
        if (aotFatbin.Length < AotSignatureValidator.SignatureSize)
        {
            throw new InvalidOperationException("AOT kernel binary is too small to contain a valid signature.");
        }

        EnsureInitialized();
        var embeddedHashStruct = new AotSignatureValidator.Hash256(aotFatbin.Slice(aotFatbin.Length - AotSignatureValidator.SignatureSize, 32));
        var cacheKey = (embeddedHashStruct, kernelName);

        if (s_functionCache.TryGetValue(cacheKey, out var lazyHit))
        {
            return lazyHit.Value.Function;
        }

        // 2. Slow path: Validate hash and load module
        var (processed, _) = AotSignatureValidator.ExtractAndVerify(aotFatbin);

        var lazyModule = s_functionCache.GetOrAdd(cacheKey, _ => new Lazy<GpuModule>(() =>
        {
            using var scope = new CudaContextScope();
            return LoadKernelFunction(processed, kernelName);
        }));

        return lazyModule.Value.Function;
    }


    private static unsafe GpuModule LoadKernelFunction(byte[] binaryData, string kernelName)
    {
        // Fatbins MUST be 8-byte aligned for cuModuleLoadDataEx
        var pAlignedBinary = NativeMemory.AlignedAlloc((nuint)binaryData.Length, 8);
        try
        {
            Marshal.Copy(binaryData, 0, (IntPtr)pAlignedBinary, binaryData.Length);

            // Do NOT pass JIT options (like ERROR_LOG_BUFFER) when loading an AOT Fatbin,
            // as it causes CUDA_ERROR_INVALID_VALUE when the driver simply loads a matching CUBIN without JITing.
            var loadResult = CudaDriverApi.cuModuleLoadDataEx(out var module, (IntPtr)pAlignedBinary, 0, null, null);
            if (loadResult != CUresult.CudaSuccess)
            {
                throw new CudaException(loadResult, $"cuModuleLoadDataEx failed with {loadResult} for kernel '{kernelName}'.");
            }

            CudaException.ThrowIfError(CudaDriverApi.cuModuleGetFunction(out var hfunc, module, kernelName));
            return new GpuModule(module, hfunc);
        }
        finally
        {
            NativeMemory.AlignedFree(pAlignedBinary);
        }
    }

    /// <summary>
    /// Loads a fatbin module directly into the CUDA driver and returns the module handle.
    /// </summary>
    /// <param name="binaryData">The raw byte array containing the Fatbin module binary.</param>
    /// <returns>A native handle (<see cref="IntPtr"/>) to the loaded CUDA module (<c>CUmodule</c>).</returns>
    public static unsafe IntPtr LoadModule(byte[] binaryData)
    {
        EnsureInitialized();

        var (processed, _) = AotSignatureValidator.ExtractAndVerify(binaryData);
        var pAlignedBinary = NativeMemory.AlignedAlloc((nuint)processed.Length, 8);
        try
        {
            Marshal.Copy(processed, 0, (IntPtr)pAlignedBinary, processed.Length);

            var loadResult = CudaDriverApi.cuModuleLoadDataEx(out var module, (IntPtr)pAlignedBinary, 0, null, null);
            if (loadResult != CUresult.CudaSuccess)
            {
                throw new CudaException(loadResult, $"cuModuleLoadDataEx failed with {loadResult} for fatbin module.");
            }
            return module;
        }
        finally
        {
            NativeMemory.AlignedFree(pAlignedBinary);
        }
    }

    /// <summary>
    /// Retrieves a function pointer from a loaded CUDA module.
    /// </summary>
    /// <param name="hModule">The handle to the loaded CUDA module (<c>CUmodule</c>).</param>
    /// <param name="kernelName">The entry point function name to retrieve.</param>
    /// <returns>A native handle (<see cref="IntPtr"/>) to the CUDA kernel function (<c>CUfunction</c>).</returns>
    public static IntPtr GetFunction(IntPtr hModule, string kernelName)
    {
        EnsureInitialized();
        CudaException.ThrowIfError(CudaDriverApi.cuModuleGetFunction(out var hfunc, hModule, kernelName));
        return hfunc;
    }

    /// <summary>
    /// Represents hardware and register resources consumed by a compiled kernel function.
    /// </summary>
    /// <param name="NumRegs">The number of registers used by each thread in the kernel.</param>
    /// <param name="LocalSizeBytes">The amount of local memory used by each thread in bytes.</param>
    /// <param name="SharedSizeBytes">The amount of shared memory used by each block in bytes.</param>
    /// <param name="ConstSizeBytes">The amount of constant memory used by the kernel in bytes.</param>
    public readonly record struct KernelResourceAttributes(
        int NumRegs,
        int LocalSizeBytes,
        int SharedSizeBytes,
        int ConstSizeBytes);

    /// <summary>
    /// Queries the hardware resource attributes of a compiled kernel function from the CUDA driver.
    /// </summary>
    /// <param name="hfunc">The native handle to the CUDA kernel function (<c>CUfunction</c>).</param>
    /// <returns>A <see cref="KernelResourceAttributes"/> containing hardware register and memory resource metrics.</returns>
    public static KernelResourceAttributes GetKernelAttributes(IntPtr hfunc)
    {
        if (hfunc == IntPtr.Zero)
        {
            throw new ArgumentException("Kernel function handle cannot be zero.", nameof(hfunc));
        }

        EnsureInitialized();
        CudaException.ThrowIfError(CudaDriverApi.cuFuncGetAttribute(out var numRegs, CuFunctionAttribute.NumRegs, hfunc));
        CudaException.ThrowIfError(CudaDriverApi.cuFuncGetAttribute(out var localBytes, CuFunctionAttribute.LocalSizeBytes, hfunc));
        CudaException.ThrowIfError(CudaDriverApi.cuFuncGetAttribute(out var sharedBytes, CuFunctionAttribute.SharedSizeBytes, hfunc));
        CudaException.ThrowIfError(CudaDriverApi.cuFuncGetAttribute(out var constBytes, CuFunctionAttribute.ConstSizeBytes, hfunc));
        return new KernelResourceAttributes(numRegs, localBytes, sharedBytes, constBytes);
    }
}
