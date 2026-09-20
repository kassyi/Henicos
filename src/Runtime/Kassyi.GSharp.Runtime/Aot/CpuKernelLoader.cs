using System.Security.Cryptography;

namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Runtime JIT compilation engine that compiles Standard C++ kernel strings into executable native CPU libraries and manages delegate bindings.
/// </summary>
public sealed class CpuKernelLoader
{

    // Cache for loaded delegates: Hash of Source and Args -> Lazy<Delegate>
    private static readonly ConcurrentDictionary<string, Lazy<Delegate>> s_functionCache = new();

    // Maintain a list of loaded module handles to free during Shutdown
    private static readonly ConcurrentBag<IntPtr> s_loadedModules = [];

    // Maintain a list of temporary files to delete during Shutdown
    private static readonly ConcurrentBag<string> s_tempFiles = [];

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
                // Ignore unload errors during shutdown
            }
        }

        s_loadedModules.Clear();

        foreach (var tempFile in s_tempFiles)
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
                // Ignore cleanup errors during shutdown
            }
        }

        s_tempFiles.Clear();
    }

    /// <summary>
    /// Loads an AOT-compiled native CPU library and returns a raw function pointer.
    /// </summary>
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
        if (fileData.Length < 32)
        {
            throw new InvalidOperationException("AOT native binary is too small to contain a valid hash.");
        }

        var embeddedHashBytes = new byte[32];
        Array.Copy(fileData, fileData.Length - 32, embeddedHashBytes, 0, 32);
        var expectedHash = Convert.ToHexStringLower(embeddedHashBytes);

        var processed = new byte[fileData.Length - 32];
        Array.Copy(fileData, 0, processed, 0, fileData.Length - 32);

        // Tamper resistance check
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(processed));
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"AOT native binary hash mismatch for '{kernelName}'. The file may be corrupted or tampered with. Expected: {expectedHash}");
        }

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
