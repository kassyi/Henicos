using Kassyi.GSharp.Runtime.Fatbin;

namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// The AOT fatbin modules and kernel functions loaded by <see cref="GpuKernelLoader"/>, one set per CUDA context.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> A module and its functions belong to the context they were loaded in. A handle used in another context
/// fails with <c>CUDA_ERROR_INVALID_HANDLE</c>, and one whose context was destroyed can crash the process inside
/// <c>cuLaunchKernel</c>. Every entry is therefore keyed by the context that a launch on the calling thread uses
/// (<see cref="GpuKernelLoader.CudaContextScope.DefaultTarget"/>), and <see cref="ForgetContext"/> drops the entries of a
/// context before it is destroyed, because the driver may give its handle to a later context.
/// </para>
/// <para>
/// <see cref="CurrentContextId"/> numbers the contexts so that a cache outside this class (a launcher's last function,
/// device memory of a library) can tell a later context that reuses a handle from the destroyed one.
/// </para>
/// </remarks>
internal static class CudaModuleCache
{
    private static readonly ConcurrentDictionary<(IntPtr Context, AotSignatureValidator.Hash256 Hash), Lazy<IntPtr>> s_modules = new();

    private static readonly ConcurrentDictionary<(IntPtr Context, AotSignatureValidator.Hash256 Hash, string KernelName), IntPtr> s_functions = new();

    private static readonly ConcurrentDictionary<IntPtr, long> s_contextIds = new();
    private static long s_lastContextId;

    /// <summary>
    /// Gets a number that identifies the CUDA context a launch on this thread uses (the context of the innermost
    /// <see cref="GpuKernelLoader.CudaContextScope"/>, otherwise <see cref="GpuKernelLoader.GlobalContext"/>), or 0 in CPU
    /// fallback mode.
    /// </summary>
    /// <remarks>
    /// The number is never reused: a context created after <see cref="GpuKernelLoader.DestroyContext"/> gets a new number
    /// even when the driver gives it the handle of the destroyed one.
    /// </remarks>
    public static long CurrentContextId
    {
        get
        {
            if (GpuKernelLoader.IsCpuFallbackMode)
            {
                return 0;
            }

            return s_contextIds.GetOrAdd(GpuKernelLoader.CudaContextScope.DefaultTarget,
                static _ => Interlocked.Increment(ref s_lastContextId));
        }
    }

    /// <summary>Returns the function <paramref name="kernelName"/> of the fatbin in the current context, loading the module there once.</summary>
    public static IntPtr GetOrLoadFunction(ReadOnlySpan<byte> aotFatbin, string kernelName)
    {
        var hash = ReadSignatureHash(aotFatbin);
        var key = (GpuKernelLoader.CudaContextScope.DefaultTarget, hash, kernelName);
        if (s_functions.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var module = GetOrLoadModule(aotFatbin);
        return s_functions.GetOrAdd(key, GpuKernelLoader.GetFunction(module, kernelName));
    }

    /// <summary>Returns the module of the fatbin in the current context, loading it there once.</summary>
    public static IntPtr GetOrLoadModule(ReadOnlySpan<byte> aotFatbin)
    {
        var hash = ReadSignatureHash(aotFatbin);
        var key = (GpuKernelLoader.CudaContextScope.DefaultTarget, hash);
        if (s_modules.TryGetValue(key, out var cached))
        {
            return cached.Value;
        }

        var (processed, _) = AotSignatureValidator.ExtractAndVerify(aotFatbin);
        var (major, minor) = GpuKernelLoader.DeviceComputeCapability;
        var fatbin = FatbinParser.TryParse(processed, out var parsed) ? parsed : null;
        if (fatbin is not null && !fatbin.IsCompatibleWith(major, minor))
        {
            throw new InvalidOperationException(GpuKernelLoader.DescribeIncompatibleFatbin(fatbin, major, minor));
        }

        var moduleId = DeviceErrorSlot.GetOrAssignModuleId(hash);
        var lazyModule = s_modules.GetOrAdd(
            key,
            k => new Lazy<IntPtr>(() => Load(k.Context, processed, fatbin, moduleId),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return lazyModule.Value;
        }
        catch
        {
            s_modules.TryRemove(key, out _);
            throw;
        }
    }

    /// <summary>
    /// Drops the modules, functions and number of <paramref name="context"/>, which is about to be destroyed; its modules
    /// go with it.
    /// </summary>
    public static void ForgetContext(IntPtr context)
    {
        s_contextIds.TryRemove(context, out _);
        foreach (var key in s_modules.Keys)
        {
            if (key.Context == context)
            {
                s_modules.TryRemove(key, out _);
            }
        }

        foreach (var key in s_functions.Keys)
        {
            if (key.Context == context)
            {
                s_functions.TryRemove(key, out _);
            }
        }

        // That cache is keyed by function handle, which a function of a later context may reuse
        DynamicSharedMemoryOptIn.Clear();
    }

    /// <summary>Unloads every loaded module in its own context; a failure is logged and the others are still unloaded.</summary>
    public static void UnloadAll()
    {
        foreach (var (key, lazyModule) in s_modules)
        {
            if (!lazyModule.IsValueCreated)
            {
                continue;
            }

            try
            {
                using var scope = new GpuKernelLoader.CudaContextScope(key.Context);
                CudaDriverApi.cuModuleUnload(lazyModule.Value);
            }
            // smell-scan: ignore swallowed-exception -- the failure is reported through Logger.Warning; shutdown
            // unloads every other module instead of stopping at the first failure
            catch (Exception ex)
            {
                Logger.Warning(nameof(GpuKernelLoader), $"Failed to unload CUDA module during shutdown: {ex.Message}");
            }
        }
    }

    /// <summary>Forgets every module and function without unloading them.</summary>
    public static void Clear()
    {
        s_functions.Clear();
        s_modules.Clear();
    }

    private static AotSignatureValidator.Hash256 ReadSignatureHash(ReadOnlySpan<byte> aotFatbin)
    {
        if (aotFatbin.Length < AotSignatureValidator.SignatureSize)
        {
            throw new InvalidOperationException("AOT kernel binary is too small to contain a valid signature.");
        }

        GpuKernelLoader.EnsureInitialized();
        return new AotSignatureValidator.Hash256(
            aotFatbin.Slice(aotFatbin.Length - AotSignatureValidator.SignatureSize, 32));
    }

    private static IntPtr Load(IntPtr context, byte[] binaryData, FatbinImage? fatbin, uint moduleId)
    {
        using var scope = new GpuKernelLoader.CudaContextScope(context);
        DeviceErrorSlot.AllocateInCurrentContext();

        // Fatbins MUST be 8-byte aligned for cuModuleLoadDataEx
        unsafe
        {
            var pAlignedBinary = NativeMemory.AlignedAlloc((nuint)binaryData.Length, 8);
            try
            {
                Marshal.Copy(binaryData, 0, (IntPtr)pAlignedBinary, binaryData.Length);

                // Do NOT pass JIT options (like ERROR_LOG_BUFFER) when loading an AOT Fatbin,
                // as it causes CUDA_ERROR_INVALID_VALUE when the driver simply loads a matching CUBIN without JITing.
                var loadResult = CudaDriverApi.cuModuleLoadDataEx(out var module, (IntPtr)pAlignedBinary, 0, null, null);
                if (loadResult != CUresult.CudaSuccess)
                {
                    var (major, minor) = GpuKernelLoader.DeviceComputeCapability;
                    throw GpuKernelLoader.CreateModuleLoadException(loadResult, fatbin, major, minor);
                }

                DeviceErrorSlot.ConfigureModule(module, moduleId);
                return module;
            }
            finally
            {
                NativeMemory.AlignedFree(pAlignedBinary);
            }
        }
    }
}
