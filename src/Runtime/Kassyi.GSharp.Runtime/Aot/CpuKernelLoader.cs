namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Loads compiled native CPU kernel libraries and manages delegate bindings.
/// </summary>
public static class CpuKernelLoader
{
    private static readonly AsyncLocal<bool> s_forceManagedExecution = new();

    // Module cache: normalized DLL path -> Lazy<IntPtr> module handle
    private static readonly ConcurrentDictionary<string, Lazy<IntPtr>> s_moduleCache = new(StringComparer.OrdinalIgnoreCase);

    // Export symbol cache: (normalized DLL path, kernel name) -> native function pointer
    private static readonly ConcurrentDictionary<(string DllPath, string KernelName), IntPtr> s_exportCache = new();

    // Typed delegate cache: (normalized DLL path, kernel name, delegate type) -> marshaled Delegate
    private static readonly ConcurrentDictionary<(string DllPath, string KernelName, Type DelegateType), Delegate> s_delegateCache = new();

    /// <summary>
    /// Gets or sets a value indicating whether to forcefully bypass native AOT DLL execution and use pure C# managed execution.
    /// </summary>
    public static bool ForceManagedExecution
    {
        get => s_forceManagedExecution.Value;
        set => s_forceManagedExecution.Value = value;
    }

    /// <summary>
    /// Determines whether the managed execution fallback should be used.
    /// </summary>
    /// <param name="dllPath">The path to the native DLL to check.</param>
    /// <returns><see langword="true"/> if managed execution should be used; otherwise, <see langword="false"/>.</returns>
    public static bool ShouldUseManagedExecution(string dllPath) => ForceManagedExecution || !File.Exists(dllPath);

    /// <summary>
    /// Unloads all cached CPU modules and clears the in-memory cache to reclaim memory.
    /// </summary>
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static void Shutdown()
    {
        s_delegateCache.Clear();
        s_exportCache.Clear();

        foreach (var pair in s_moduleCache)
        {
            if (!pair.Value.IsValueCreated)
            {
                continue;
            }

            try
            {
                NativeLibrary.Free(pair.Value.Value);
            }
            catch (Exception ex)
            {
                Logger.Warning(nameof(CpuKernelLoader), $"Failed to free native library '{pair.Key}' during shutdown: {ex.Message}");
            }
        }

        s_moduleCache.Clear();
    }

    /// <summary>
    /// Loads an AOT-compiled native CPU library and returns a raw function pointer.
    /// </summary>
    /// <param name="kernelName">The name of the exported kernel function.</param>
    /// <param name="dllPath">The file path to the native dynamic library.</param>
    /// <returns>A native function pointer (<see cref="IntPtr"/>) to the exported kernel.</returns>
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static IntPtr LoadAotKernelPtr(string kernelName, string dllPath)
    {
        var fullPath = Path.GetFullPath(dllPath);
        var exportKey = (fullPath, kernelName);
        if (s_exportCache.TryGetValue(exportKey, out var cachedPtr))
        {
            return cachedPtr;
        }

        var module = GetOrLoadModule(fullPath);
        if (!NativeLibrary.TryGetExport(module, kernelName, out var hfunc))
        {
            throw new InvalidOperationException(
                $"Failed to find exported function '{kernelName}' in {dllPath}. Did you forget extern \"C\" __declspec(dllexport)?");
        }

        return s_exportCache.GetOrAdd(exportKey, hfunc);
    }

    /// <summary>
    /// Loads an AOT-compiled native CPU library and returns a strongly-typed delegate.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type matching the native kernel signature.</typeparam>
    /// <param name="kernelName">The name of the exported kernel function.</param>
    /// <param name="dllPath">The file path to the native dynamic library.</param>
    /// <returns>A strongly-typed delegate of type <typeparamref name="TDelegate"/> wrapping the native kernel function.</returns>
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static TDelegate LoadAotKernel<TDelegate>(string kernelName, string dllPath)
        where TDelegate : Delegate
    {
        var fullPath = Path.GetFullPath(dllPath);
        var delegateKey = (fullPath, kernelName, typeof(TDelegate));
        if (s_delegateCache.TryGetValue(delegateKey, out var cachedDelegate))
        {
            return (TDelegate)cachedDelegate;
        }

        var fnPtr = LoadAotKernelPtr(kernelName, fullPath);
        var typedDelegate = Marshal.GetDelegateForFunctionPointer<TDelegate>(fnPtr);
        return (TDelegate)s_delegateCache.GetOrAdd(delegateKey, typedDelegate);
    }

    private static IntPtr GetOrLoadModule(string fullPath)
    {
        var lazyModule = s_moduleCache.GetOrAdd(
            fullPath,
            static path => new Lazy<IntPtr>(() => LoadVerifiedModule(path), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return lazyModule.Value;
        }
        catch
        {
            s_moduleCache.TryRemove(fullPath, out _);
            throw;
        }
    }

    private static IntPtr LoadVerifiedModule(string fullPath)
    {
        var fileData = File.ReadAllBytes(fullPath);

        // Validate embedded signature. Throws InvalidOperationException on mismatch.
        _ = AotSignatureValidator.ExtractAndVerify(fileData);

        return NativeLibrary.Load(fullPath);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetLastErrorDelegate(out IntPtr outMessage);

    /// <summary>
    /// Checks the native runtime error slot in the specified CPU AOT library and throws the corresponding managed exception if an error occurred.
    /// </summary>
    /// <param name="dllPath">The file path to the native dynamic library.</param>
    public static void ThrowIfNativeError(string dllPath)
    {
        var consumeLastError = LoadAotKernel<GetLastErrorDelegate>("__gsharp_consume_last_error", dllPath);
        var errorCode = consumeLastError(out var msgPtr);
        if (errorCode == 0)
        {
            return;
        }

        var message = msgPtr != IntPtr.Zero
            ? Marshal.PtrToStringAnsi(msgPtr) ?? "Unknown G# CPU runtime error."
            : "Unknown G# CPU runtime error.";

        throw errorCode switch
        {
            1 => new NotSupportedException(message),
            2 => new InvalidOperationException(message),
            3 => new IndexOutOfRangeException(message),
            4 => new ArgumentOutOfRangeException(null, message),
            5 => new ArgumentException(message),
            _ => new InvalidOperationException(message)
        };
    }
}
