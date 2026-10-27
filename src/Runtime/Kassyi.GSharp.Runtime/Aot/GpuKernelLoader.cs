using Kassyi.GSharp.Runtime.Fatbin;

namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Manages CUDA thread contexts, module loading, and function pointer caches for compiled GPU kernels.
/// </summary>
// ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
public static class GpuKernelLoader
{
    private static volatile bool s_isGlobalInit;
    private static IntPtr s_globalContext;
    private static readonly Lock s_initLock = new();
    private static volatile bool s_isCpuFallbackMode;
    private static int s_deviceMajor;
    private static int s_deviceMinor;

    /// <summary>
    /// Gets the global CUDA context handle.
    /// </summary>
    public static IntPtr GlobalContext
    {
        get
        {
            EnsureInitialized();
            return s_globalContext;
        }
    }

    private static readonly AsyncLocal<bool> s_forceCpuFallback = new();

    /// <summary>
    /// Gets or sets a value indicating whether to forcibly emulate GPU runtime on CPU host memory.
    /// </summary>
    public static bool ForceCpuFallback
    {
        get => s_forceCpuFallback.Value;
        set => s_forceCpuFallback.Value = value;
    }

    /// <summary>
    /// Gets a value indicating whether the runtime is operating in CPU host fallback mode.
    /// </summary>
    public static bool IsCpuFallbackMode
    {
        get
        {
            if (ForceCpuFallback)
            {
                return true;
            }

            EnsureInitialized();
            return s_isCpuFallbackMode;
        }
    }

    /// <summary>
    /// Scoped RAII structure that activates a CUDA context on the current thread and restores the previous context upon disposal.
    /// </summary>
    public readonly struct CudaContextScope : IDisposable
    {
        [ThreadStatic]
        private static IntPtr t_activeContext;

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
            _prevActiveContext = t_activeContext;

            _targetContext = customContext != IntPtr.Zero
                ? customContext
                : (t_activeContext != IntPtr.Zero ? t_activeContext : s_globalContext);

            t_activeContext = _targetContext;

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

            t_activeContext = _prevActiveContext;

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

    /// <summary>
    /// Ensures that the global CUDA driver and primary context are initialized in a thread-safe manner.
    /// Falls back to CPU emulation mode if the CUDA driver or hardware is unavailable.
    /// </summary>
    public static void EnsureInitialized()
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

            try
            {
                CudaException.ThrowIfError(CudaDriverApi.cuInit(0));
                CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out var device, 0));

                CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out s_globalContext, 0, device));
                CudaException.ThrowIfError(CudaDriverApi.cuCtxPopCurrent_v2(out _)); // Immediately pop to avoid poisoning the ThreadPool thread

                CudaException.ThrowIfError(CudaDriverApi.cuDeviceGetAttribute(out s_deviceMajor,
                    CudaDriverApi.CuDeviceAttribute.ComputeCapabilityMajor, device));
                CudaException.ThrowIfError(CudaDriverApi.cuDeviceGetAttribute(out s_deviceMinor,
                    CudaDriverApi.CuDeviceAttribute.ComputeCapabilityMinor, device));
                s_isCpuFallbackMode = false;
            }
            catch (DllNotFoundException)
            {
                s_isCpuFallbackMode = true;
            }
            catch (CudaException ex)
            {
                Logger.Warning(nameof(GpuKernelLoader), $"EnsureInitialized failed: {ex.Message}");
                s_isCpuFallbackMode = true;
            }

            s_isGlobalInit = true;
        }
    }

    // Module cache indexed by 256-bit signature hash to avoid duplicate cuModuleLoadDataEx calls per fatbin
    private static readonly ConcurrentDictionary<AotSignatureValidator.Hash256, Lazy<IntPtr>> s_moduleCache = new();

    // Function cache indexed by (Hash256, KernelName)
    private static readonly ConcurrentDictionary<(AotSignatureValidator.Hash256 Hash, string KernelName), IntPtr> s_functionCache = new();

#pragma warning disable IDE0051 // Justification: Public API for library
    /// <summary>
    /// Unloads all cached GPU modules and clears the module cache.
    /// </summary>
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static void Shutdown()
    {
        s_functionCache.Clear();

        if (!s_isGlobalInit)
        {
            s_moduleCache.Clear();
            return;
        }

        try
        {
            using var scope = new CudaContextScope();

            foreach (var lazyModule in s_moduleCache.Values)
            {
                if (!lazyModule.IsValueCreated)
                {
                    continue;
                }

                try
                {
                    CudaDriverApi.cuModuleUnload(lazyModule.Value);
                }
                catch (Exception ex)
                {
                    Logger.Warning(nameof(GpuKernelLoader), $"Failed to unload CUDA module during shutdown: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(nameof(GpuKernelLoader), $"Context initialization failed during shutdown: {ex.Message}");
        }
        finally
        {
            s_moduleCache.Clear();
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
    /// Faults every pending stream wait on <paramref name="context"/>, then destroys the context with <c>cuCtxDestroy</c>.
    /// </summary>
    /// <param name="context">A context created with <c>cuCtxCreate</c>. It must not be used after this call.</param>
    /// <exception cref="CudaException"><c>cuCtxDestroy</c> failed.</exception>
    /// <exception cref="TimeoutException">A watchdog tick did not leave the driver in time; the context was not destroyed.</exception>
    /// <remarks>
    /// <para>
    /// <b>[WHY]:</b> <see cref="CudaStreamCompletion.WaitAsync"/> polls the awaited stream from a watchdog timer that makes
    /// the stream's context current. The driver forbids using a context while <c>cuCtxDestroy</c> runs (it drains
    /// outstanding work first), and a destroyed handle crashes the process instead of returning an error. Destroying a
    /// context through this method detaches those waits before the driver call.
    /// </para>
    /// <para>
    /// Destroying a context with <c>cuCtxDestroy</c> directly bypasses this protection while a wait is pending.
    /// </para>
    /// <para>
    /// Awaiters of the faulted waits observe an <see cref="ObjectDisposedException"/>. The GPU work they were waiting for
    /// is not canceled; it is drained or torn down together with the context.
    /// </para>
    /// </remarks>
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static void DestroyContext(IntPtr context)
    {
        CudaStreamCompletionSource.AbandonContext(context);
        CudaException.ThrowIfError(CudaDriverApi.cuCtxDestroy_v2(context));
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
        if (aotFatbin.Length < AotSignatureValidator.SignatureSize)
        {
            throw new InvalidOperationException("AOT kernel binary is too small to contain a valid signature.");
        }

        EnsureInitialized();
        var embeddedHash = new AotSignatureValidator.Hash256(
            aotFatbin.Slice(aotFatbin.Length - AotSignatureValidator.SignatureSize, 32));
        var cacheKey = (embeddedHash, kernelName);

        if (s_functionCache.TryGetValue(cacheKey, out var cachedFunction))
        {
            return cachedFunction;
        }

        var module = GetOrLoadModule(aotFatbin);
        var hfunc = GetFunction(module, kernelName);
        return s_functionCache.GetOrAdd(cacheKey, hfunc);
    }

    /// <summary>
    /// Loads a fatbin module directly into the CUDA driver and returns the module handle.
    /// </summary>
    /// <param name="binaryData">The raw byte array containing the Fatbin module binary.</param>
    /// <returns>A native handle (<see cref="IntPtr"/>) to the loaded CUDA module (<c>CUmodule</c>).</returns>
    public static IntPtr LoadModule(byte[] binaryData)
    {
        ArgumentNullException.ThrowIfNull(binaryData);
        return GetOrLoadModule(binaryData);
    }

    private static IntPtr GetOrLoadModule(ReadOnlySpan<byte> aotFatbin)
    {
        if (aotFatbin.Length < AotSignatureValidator.SignatureSize)
        {
            throw new InvalidOperationException("AOT kernel binary is too small to contain a valid signature.");
        }

        EnsureInitialized();
        var embeddedHash = new AotSignatureValidator.Hash256(
            aotFatbin.Slice(aotFatbin.Length - AotSignatureValidator.SignatureSize, 32));

        if (s_moduleCache.TryGetValue(embeddedHash, out var cachedModule))
        {
            return cachedModule.Value;
        }

        var (processed, _) = AotSignatureValidator.ExtractAndVerify(aotFatbin);

        var fatbin = FatbinParser.TryParse(processed, out var parsed) ? parsed : null;
        if (fatbin is not null && !fatbin.IsCompatibleWith(s_deviceMajor, s_deviceMinor))
        {
            throw new InvalidOperationException(DescribeIncompatibleFatbin(fatbin, s_deviceMajor, s_deviceMinor));
        }

        var lazyModule = s_moduleCache.GetOrAdd(
            embeddedHash,
            _ => new Lazy<IntPtr>(() => LoadModuleCore(processed, fatbin), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return lazyModule.Value;
        }
        catch
        {
            s_moduleCache.TryRemove(embeddedHash, out _);
            throw;
        }
    }

    private static unsafe IntPtr LoadModuleCore(byte[] binaryData, FatbinImage? fatbin)
    {
        using var scope = new CudaContextScope();

        // Fatbins MUST be 8-byte aligned for cuModuleLoadDataEx
        var pAlignedBinary = NativeMemory.AlignedAlloc((nuint)binaryData.Length, 8);
        try
        {
            Marshal.Copy(binaryData, 0, (IntPtr)pAlignedBinary, binaryData.Length);

            // Do NOT pass JIT options (like ERROR_LOG_BUFFER) when loading an AOT Fatbin,
            // as it causes CUDA_ERROR_INVALID_VALUE when the driver simply loads a matching CUBIN without JITing.
            var loadResult = CudaDriverApi.cuModuleLoadDataEx(out var module, (IntPtr)pAlignedBinary, 0, null, null);
            return loadResult != CUresult.CudaSuccess
                ? throw CreateModuleLoadException(loadResult, fatbin, s_deviceMajor, s_deviceMinor)
                : module;
        }
        finally
        {
            NativeMemory.AlignedFree(pAlignedBinary);
        }
    }

    /// <summary>Creates the exception for a failed <c>cuModuleLoadDataEx</c> call on a fatbin module.</summary>
    /// <remarks>
    /// <see cref="FatbinItemInfo.IsCompatibleWith"/> accepts everything the driver may accept, so the driver can still
    /// refuse a module that passed it (an architecture-specific cuBIN such as <c>sm_90a</c>, or a desktop cuBIN on a
    /// Tegra GPU). That refusal (<see cref="CUresult.CudaErrorNoBinaryForGpu"/>) gets the same message as the pre-check.
    /// A driver older than the toolkit that wrote the PTX cannot JIT-compile it
    /// (<see cref="CUresult.CudaErrorUnsupportedPtxVersion"/>); that message names the cause and the two fixes. Both keep
    /// the driver's error as the inner exception.
    /// </remarks>
    internal static Exception CreateModuleLoadException(CUresult result, FatbinImage? fatbin, int deviceMajor,
        int deviceMinor)
    {
        var driverError = new CudaException(result, $"cuModuleLoadDataEx failed with {result} for fatbin module.");
        return result switch
        {
            CUresult.CudaErrorNoBinaryForGpu =>
                new InvalidOperationException(DescribeIncompatibleFatbin(fatbin, deviceMajor, deviceMinor), driverError),
            CUresult.CudaErrorUnsupportedPtxVersion =>
                new InvalidOperationException(DescribeUnsupportedPtx(fatbin, deviceMajor, deviceMinor), driverError),
            _ => driverError
        };
    }

    /// <summary>Describes a fatbin module that holds no image the current GPU can run, listing the images it holds.</summary>
    internal static string DescribeIncompatibleFatbin(FatbinImage? fatbin, int deviceMajor, int deviceMinor)
    {
        var message = new StringBuilder();
        message.Append(
            $"The FATBIN module does not contain any PTX or cuBIN compatible with the current GPU (sm_{deviceMajor}{deviceMinor}). ");
        AppendContents(message, fatbin);
        AppendArchitectureHint(message, deviceMajor, deviceMinor);
        return message.ToString();
    }

    /// <summary>Describes a fatbin module whose PTX the driver is too old to JIT-compile.</summary>
    internal static string DescribeUnsupportedPtx(FatbinImage? fatbin, int deviceMajor, int deviceMinor)
    {
        var message = new StringBuilder();
        message.Append(
            $"The GPU driver cannot JIT-compile the PTX of the FATBIN module for the current GPU (sm_{deviceMajor}{deviceMinor}) because the driver is older than the CUDA Toolkit that built the module. ");
        AppendContents(message, fatbin);
        message.Append(" Update the NVIDIA driver, or build native code for this GPU so that no JIT is needed:");
        AppendArchitectureHint(message, deviceMajor, deviceMinor);
        return message.ToString();
    }

    private static void AppendContents(StringBuilder message, FatbinImage? fatbin)
    {
        if (fatbin is null)
        {
            message.Append("Its contents are unknown because the FATBIN header could not be parsed.");
            return;
        }

        if (fatbin.Items.Count == 0)
        {
            message.Append("It contains no images.");
            return;
        }

        message.Append("It contains: ");
        for (var i = 0; i < fatbin.Items.Count; i++)
        {
            var item = fatbin.Items[i];
            if (i > 0)
            {
                message.Append(", ");
            }

            message.Append(item.Type switch
            {
                FatbinItemInfo.TypePtx => $"PTX compute_{item.Major}{item.Minor}",
                FatbinItemInfo.TypeCubin => $"cuBIN sm_{item.Major}{item.Minor}",
                _ => $"type {item.Type} ({item.ComputeVersion})"
            });
        }

        message.Append('.');
    }

    // The default entries stay in the list: setting the property replaces the default instead of adding to it
    private static void AppendArchitectureHint(StringBuilder message, int deviceMajor, int deviceMinor) =>
        message.Append(
            $" Add this architecture to the GSharpTargetArchitectures MSBuild property and keep the default entries (for example compute_86,sm_86;compute_86,compute_86;compute_{deviceMajor}{deviceMinor},sm_{deviceMajor}{deviceMinor}).");

    /// <summary>
    /// Retrieves a function pointer from a loaded CUDA module.
    /// </summary>
    /// <param name="hModule">The handle to the loaded CUDA module (<c>CUmodule</c>).</param>
    /// <param name="kernelName">The entry point function name to retrieve.</param>
    /// <returns>A native handle (<see cref="IntPtr"/>) to the CUDA kernel function (<c>CUfunction</c>).</returns>
    public static IntPtr GetFunction(IntPtr hModule, string kernelName)
    {
        using var scope = new CudaContextScope();
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
        CudaException.ThrowIfError(
            CudaDriverApi.cuFuncGetAttribute(out var numRegs, CuFunctionAttribute.NumRegs, hfunc));
        CudaException.ThrowIfError(
            CudaDriverApi.cuFuncGetAttribute(out var localBytes, CuFunctionAttribute.LocalSizeBytes, hfunc));
        CudaException.ThrowIfError(CudaDriverApi.cuFuncGetAttribute(out var sharedBytes,
            CuFunctionAttribute.SharedSizeBytes, hfunc));
        CudaException.ThrowIfError(
            CudaDriverApi.cuFuncGetAttribute(out var constBytes, CuFunctionAttribute.ConstSizeBytes, hfunc));
        return new KernelResourceAttributes(numRegs, localBytes, sharedBytes, constBytes);
    }
}
