namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Runtime JIT compilation engine that compiles Standard C++ kernel strings into executable native CPU libraries and manages delegate bindings.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class CpuKernelLoader
{
    private static readonly AsyncLocal<bool> _forceManagedExecution = new();
    
    /// <summary>
    /// Gets or sets a value indicating whether to forcefully bypass native AOT DLL execution and use pure C# managed execution.
    /// </summary>
    public static bool ForceManagedExecution
    {
        get => _forceManagedExecution.Value;
        set => _forceManagedExecution.Value = value;
    }

    /// <summary>
    /// Determines whether the managed execution fallback should be used.
    /// </summary>
    /// <param name="dllPath">The path to the native DLL to check.</param>
    /// <returns><see langword="true"/> if managed execution should be used; otherwise, <see langword="false"/>.</returns>
    public static bool ShouldUseManagedExecution(string dllPath) => ForceManagedExecution || !File.Exists(dllPath);

    // Cache for loaded delegates: Hash of Source and Args -> Lazy<Delegate>
    private static readonly ConcurrentDictionary<string, Lazy<Delegate>> s_functionCache = new();

    // Maintain a list of loaded module handles to free during Shutdown
    private static readonly ConcurrentBag<IntPtr> s_loadedModules = [];


    /// <summary>
    /// Unloads all cached CPU modules and clears the in-memory cache to reclaim memory.
    /// </summary>
    // ReSharper disable once UnusedMember.Global -- Public API exposed for runtime consumers
    public static void Shutdown()
    {
        s_functionCache.Clear();

        foreach (var module in s_loadedModules)
        {
            try
            {
                NativeLibrary.Free(module);
            }
            catch
            {
                // Best-effort cleanup during AppDomain shutdown; exceptions are intentionally ignored.
            }
        }

        s_loadedModules.Clear();
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
        var cacheKey = dllPath + "::" + kernelName;

        var lazyPtr = s_functionCache.GetOrAdd(cacheKey, _ => new Lazy<Delegate>(() =>
        {
            return new Func<IntPtr>(() => LoadKernelFunctionPtr(dllPath, kernelName));
        }));

        if (lazyPtr.Value is Func<IntPtr> ptrFunc)
        {
            return ptrFunc();
        }

        throw new Exception("Cache type mismatch");
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
        var cacheKey = dllPath + "::" + kernelName;

        if (s_functionCache.TryGetValue(cacheKey, out var lazyHit))
        {
            return (TDelegate)lazyHit.Value;
        }

        var lazyDelegate = s_functionCache.GetOrAdd(cacheKey,
            _ => new Lazy<Delegate>(() => LoadKernelFunction<TDelegate>(dllPath, kernelName)));

        return (TDelegate)lazyDelegate.Value;
    }

    private static IntPtr LoadKernelFunctionPtr(string dllPath, string kernelName)
    {
        var fileData = File.ReadAllBytes(dllPath);

        // Validate embedded signature. Throws InvalidOperationException on mismatch.
        _ = AotSignatureValidator.ExtractAndVerify(fileData);

        var loadPath = dllPath;

        var module = NativeLibrary.Load(loadPath);
        s_loadedModules.Add(module);

        if (!NativeLibrary.TryGetExport(module, kernelName, out var hfunc))
        {
            throw new Exception(
                $"Failed to find exported function '{kernelName}' in {loadPath}. Did you forget extern \"C\" __declspec(dllexport)?");
        }

        return hfunc;
    }

    private static TDelegate LoadKernelFunction<TDelegate>(string dllPath, string kernelName)
        where TDelegate : Delegate =>
        Marshal.GetDelegateForFunctionPointer<TDelegate>(LoadKernelFunctionPtr(dllPath, kernelName));
}
